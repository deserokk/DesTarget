using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace DesTarget.Targeting;

internal static class Probe {
	internal static string Path
		=> System.IO.Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "probe.log");

	internal static void Write(string note) {
		try {
			Directory.CreateDirectory(Plugin.PluginInterface.ConfigDirectory.FullName);

			var player = Plugin.Objects.LocalPlayer;
			if (player is null) {
				Plugin.Chat.PrintError("[DesTarget] no local player.");
				return;
			}

			var found = Candidates.Gather(float.MaxValue, measure: true).OrderBy(c => c.Distance).ToList();
			var tuning = Tuning.For(player);

			var text = new StringBuilder();
			text.AppendLine();
			text.AppendLine($"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} {note} ===");
			text.AppendLine($"zone {Plugin.ClientState.TerritoryType}, pvp {Plugin.ClientState.IsPvP}, "
				+ $"job role {(tuning.Melee ? "melee" : "ranged")}, reach {tuning.Reach:0.#}, "
				+ $"fade {tuning.NearFadeYalms:0.#}, aim {tuning.AimFadeDegrees:0}deg/{tuning.AimWeight:0.00}");
			text.AppendLine($"{found.Count} attackable, no distance ceiling applied");
			text.AppendLine("  dist   off  inView onScreen   hp   kind  typeId");

			foreach (var c in found)
				text.AppendLine(string.Format(CultureInfo.InvariantCulture,
					"{0,6:0.0} {1,5:0} {2,7} {3,8} {4,5:0}% {5,5} {6,7}",
					c.Distance, c.Angle * 180f / MathF.PI, c.InViewRange, c.OnScreen,
					c.Health * 100f, c.Kind, c.BaseId));

			File.AppendAllText(Path, text.ToString());

			Plugin.Chat.Print($"[DesTarget] wrote {found.Count} rows to probe.log.");
			Plugin.Log.Information($"DesTarget probe: {found.Count} rows -> {Path}");
		}
		catch (Exception ex) {
			Plugin.Chat.PrintError($"[DesTarget] probe failed: {ex.Message}");
			Plugin.Log.Error(ex, "DesTarget: probe failed.");
		}
	}
}
