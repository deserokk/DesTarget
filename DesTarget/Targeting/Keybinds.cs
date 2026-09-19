using System;
using System.IO;
using System.Text;

using FFXIVClientStructs.FFXIV.Client.System.Input;

using CSFramework = FFXIVClientStructs.FFXIV.Client.System.Framework.Framework;

namespace DesTarget.Targeting;

internal static unsafe class Keybinds {

	private const int Tab = 9;

	private static string Safe(System.Reflection.FieldInfo field, object on) {
		try {
			return field.GetValue(on)?.ToString() ?? "null";
		}
		catch {
			return "?";
		}
	}

	private const int CycleRow = 366;

	private const int ExpectedRows = 679;

	private static bool Check(Span<Keybind> span, out string why) {
		if (span.Length == ExpectedRows) {
			why = string.Empty;
			return true;
		}

		why = $"the keybind table has {span.Length} rows, not {ExpectedRows}, so a patch may have moved it";
		return false;
	}

	internal static string LastRefusal { get; private set; } = string.Empty;

	internal static bool Apply(out string said) {
		said = string.Empty;

		try {
			var ui = CSFramework.Instance()->GetUIModule();
			var input = ui == null ? null : ui->GetUIInputData();

			if (input == null) {
				said = "the game's input data is not ready";
				return false;
			}

			var span = input->GetKeybindSpan();

			if (span.Length <= CycleRow) {
				said = "the keybind table is not loaded yet";
				return false;
			}

			var keys = span[CycleRow].KeySettings;
			var slot = keys.Length > 0 && keys[0].Key != SeVirtualKey.NO_KEY ? 0
				: keys.Length > 1 && keys[1].Key != SeVirtualKey.NO_KEY ? 1 : -1;

			if (slot < 0) return false;

			if (!Check(span, out var why)) {
				said = "refused: " + why;
				LastRefusal = why;
				Plugin.Log.Warning($"DesTarget: keybind takeover refused, {why}");
				return false;
			}

			var taken = keys[slot].Key;
			var mod = keys[slot].KeyModifier;

			var asKey = (Dalamud.Game.ClientState.Keys.VirtualKey)(int)taken;
			if (!Enum.IsDefined(asKey)) {
				said = $"the game's key ({taken}) has no equivalent we can bind";
				return false;
			}

			Plugin.Config.Cycle.Key = asKey;
			Plugin.Config.Cycle.Ctrl = mod == KeyModifierFlag.Ctrl;
			Plugin.Config.Cycle.Alt = mod == KeyModifierFlag.Alt;
			Plugin.Config.Cycle.Shift = mod == KeyModifierFlag.Shift;

			Plugin.Config.TakenKey = (int)taken;
			Plugin.Config.TakenModifier = (int)mod;
			Plugin.Config.Save();

			for (var k = 0; k < keys.Length; k++) {
				keys[k].Key = SeVirtualKey.NO_KEY;
				keys[k].KeyModifier = KeyModifierFlag.None;
			}

			LastRefusal = string.Empty;
			said = $"took {Name(taken, mod)} from the game.";
			Plugin.Log.Information($"DesTarget: took the cycle keybind ({Name(taken, mod)}).");
			return true;
		}
		catch (Exception ex) {
			said = $"{ex.GetType().Name}: {ex.Message}";
			Plugin.Log.Error(ex, "DesTarget: keybind takeover failed.");
			return false;
		}
	}

	internal static string GiveBack() {
		try {
			var ui = CSFramework.Instance()->GetUIModule();
			var input = ui == null ? null : ui->GetUIInputData();
			if (input == null) return "it will be back next time you log in.";

			var span = input->GetKeybindSpan();
			if (span.Length <= CycleRow || !Check(span, out _)) return "it will be back next time you log in.";

			var key = (SeVirtualKey)Plugin.Config.TakenKey;
			var mod = (KeyModifierFlag)Plugin.Config.TakenModifier;
			if (key == SeVirtualKey.NO_KEY) return "there was nothing to give back.";

			var keys = span[CycleRow].KeySettings;
			if (keys.Length == 0) return "it will be back next time you log in.";

			keys[0].Key = key;
			keys[0].KeyModifier = mod;

			return $"gave {Name(key, mod)} back to the game.";
		}
		catch (Exception ex) {
			Plugin.Log.Error(ex, "DesTarget: could not give the cycle keybind back.");
			return "it will be back next time you log in.";
		}
	}

	private static string Name(SeVirtualKey key, KeyModifierFlag mod)
		=> (mod == KeyModifierFlag.None ? string.Empty : mod + "+") + key;

	internal static void Dump() {
		try {
			var ui = CSFramework.Instance()->GetUIModule();
			var input = ui == null ? null : ui->GetUIInputData();
			if (input == null) {
				Plugin.Chat.PrintError("[DesTarget] no UIInputData.");
				return;
			}

			var text = new StringBuilder();
			text.AppendLine();
			text.AppendLine($"=== keybinds {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");

			var span = input->GetKeybindSpan();
			text.AppendLine($"{span.Length} keybinds in the table");

			const System.Reflection.BindingFlags All = System.Reflection.BindingFlags.Public
				| System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

			var setting = span.Length > 0 && span[0].KeySettings.Length > 0 ? span[0].KeySettings[0] : default;
			var fields = setting.GetType().GetFields(All);

			text.AppendLine($"KeySetting: {string.Join(", ", Array.ConvertAll(fields, f => $"{f.FieldType.Name} {f.Name}"))}");

			var bound = 0;

			for (var i = 0; i < span.Length; i++) {
				var keys = span[i].KeySettings;
				var parts = new System.Collections.Generic.List<string>();

				for (var k = 0; k < keys.Length; k++) {
					object one = keys[k];
					var described = string.Join(",", Array.ConvertAll(fields, f => $"{f.Name}={Safe(f, one)}"));

					if (described.Replace("=0", string.Empty).Replace(",", string.Empty)
					             .Replace("Key", string.Empty).Length == described.Length) continue;

					parts.Add($"[{k}] {described}");
				}

				if (parts.Count == 0) continue;

				bound++;
				text.AppendLine($"  {i,4}: {string.Join("  ", parts)}");
			}

			text.AppendLine($"{bound} rows have something bound");

			var path = Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "probe.log");
			Directory.CreateDirectory(Plugin.PluginInterface.ConfigDirectory.FullName);
			File.AppendAllText(path, text.ToString());

			Plugin.Chat.Print($"[DesTarget] {bound} bound keybinds written to probe.log.");
		}
		catch (Exception ex) {
			Plugin.Chat.PrintError($"[DesTarget] keybind probe failed: {ex.Message}");
			Plugin.Log.Error(ex, "DesTarget: keybind probe failed.");
		}
	}
}
