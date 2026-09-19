using System;
using System.Numerics;

using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.IoC;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;

using FFXIVClientStructs.FFXIV.Client.Game;

using DesTarget.Input;
using DesTarget.Targeting;

namespace DesTarget;

public sealed unsafe class Plugin: IDalamudPlugin {
	[PluginService] internal static IDalamudPluginInterface PluginInterface { get; private set; } = null!;
	[PluginService] internal static ICommandManager Commands { get; private set; } = null!;
	[PluginService] internal static IFramework Framework { get; private set; } = null!;
	[PluginService] internal static IObjectTable Objects { get; private set; } = null!;
	[PluginService] internal static IClientState ClientState { get; private set; } = null!;
	[PluginService] internal static ITargetManager Targets { get; private set; } = null!;
	[PluginService] internal static IChatGui Chat { get; private set; } = null!;
	[PluginService] internal static IKeyState Keys { get; private set; } = null!;
	[PluginService] internal static IPluginLog Log { get; private set; } = null!;
	[PluginService] internal static IDataManager Data { get; private set; } = null!;
	[PluginService] internal static IGameGui GameGui { get; private set; } = null!;
	[PluginService] internal static IGameInteropProvider Interop { get; private set; } = null!;

	internal static Configuration Config { get; private set; } = null!;

	private const string CommandName = "/destarget";

	private readonly WindowSystem windows = new("DesTarget");
	private readonly KeybindWatcher watcher = new();
	private readonly Picker picker = new();
	private readonly MainWindow window;

	private delegate bool UseActionLocationDelegate(ActionManager* self, ActionType type, uint id,
	                                                ulong target, Vector3* location, uint extra, byte a7);

	private readonly Hook<UseActionLocationDelegate>? actionHook;

	private ulong tickTarget;

	private const ulong NoTarget = 0xE0000000;

	private static bool StillWorthHitting(ulong targetId) {
		if (targetId is 0 or >= NoTarget) return false;

		var obj = Objects.SearchById(targetId);
		if (obj is null || !obj.IsTargetable || obj.IsDead) return false;

		return obj is not Dalamud.Game.ClientState.Objects.Types.IBattleChara chara || chara.CurrentHp > 0;
	}

	public Plugin() {
		Config = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
		Config.Migrate();

		this.window = new MainWindow(this.picker);
		this.windows.AddWindow(this.window);

		this.watcher.Register("cycle", "Cycle targets", () => Config.Cycle, this.picker.Next);

		Commands.AddHandler(CommandName, new CommandInfo(this.OnCommand) {
			HelpMessage = "Open DesTarget.",
		});

		PluginInterface.UiBuilder.Draw += this.OnDraw;
		PluginInterface.UiBuilder.OpenConfigUi += this.Open;
		PluginInterface.UiBuilder.OpenMainUi += this.Open;
		Framework.Update += this.OnFramework;
		ClientState.Login += this.OnLogin;
		ClientState.TerritoryChanged += this.OnZone;

		if (ClientState.IsLoggedIn) this.OnLogin();

		var address = (nint)ActionManager.MemberFunctionPointers.UseActionLocation;
		if (address == nint.Zero) {

			Log.Warning("DesTarget: could not find UseActionLocation. The cycle will reset on the clock only.");
		}
		else {
			this.actionHook = Interop.HookFromAddress<UseActionLocationDelegate>(address, this.OnAction);
			this.actionHook.Enable();
		}

		NameTheAttackCheck();
	}

	private static void NameTheAttackCheck() {
		try {
			var row = Data.GetExcelSheet<Lumina.Excel.Sheets.Action>()
			              .GetRowOrDefault(Candidates.AttackAction);

			Log.Information(row is null
				? $"DesTarget: attack check uses action {Candidates.AttackAction}, which is not in the Action sheet."
				: $"DesTarget: attack check uses action {Candidates.AttackAction} \"{row.Value.Name.ExtractText()}\", "
				  + $"range {row.Value.Range}, targets enemies: {row.Value.CanTargetHostile}.");
		}
		catch (Exception ex) {
			Log.Warning($"DesTarget: could not look up action {Candidates.AttackAction}. {ex.Message}");
		}
	}

	private void OnDraw() {
		Overlay.Draw();
		this.windows.Draw();
	}

	private bool OnAction(ActionManager* self, ActionType type, uint id, ulong target,
	                      Vector3* location, uint extra, byte a7) {

		var current = Targets.Target?.GameObjectId ?? 0;
		var hadTarget = current != 0;
		var needsEnemy = type == ActionType.Action && NeedsEnemy(id);
		var targetIn = target;

		var gameChose = this.tickTarget == 0 && current != 0;

		if (Config.PickWhenNoTarget && needsEnemy && (gameChose || (!hadTarget && !StillWorthHitting(target)))) {
			var chosen = this.picker.PickForAction();
			if (chosen != 0) target = chosen;
		}

		if (this.picker.Recorder.Running)
			this.picker.Recorder.Hooked(ActionName(id), id, (int)type, targetIn, target,
			                            hadTarget && !gameChose, needsEnemy);

		var before = self->LastUsedActionSequence;
		var used = this.actionHook!.Original(self, type, id, target, location, extra, a7);

		if (used && self->LastUsedActionSequence != before) this.picker.NoticeAction();

		return used;
	}

	private static bool NeedsEnemy(uint actionId) {
		if (EnemyActions.TryGetValue(actionId, out var known)) return known;

		var row = Data.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(actionId);
		var wants = row is { } action && action.CanTargetHostile && !action.TargetArea;

		EnemyActions[actionId] = wants;
		return wants;
	}

	private static readonly System.Collections.Generic.Dictionary<uint, bool> EnemyActions = new();

	private static string ActionName(uint actionId) {
		var row = Data.GetExcelSheet<Lumina.Excel.Sheets.Action>().GetRowOrDefault(actionId);
		var name = row?.Name.ExtractText() ?? string.Empty;

		return name.Length == 0 ? "?" : name;
	}

	private DateTime tryTakingUntil = DateTime.MinValue;

	private void OnZone(uint territory) => this.picker.Recorder.Zone(territory);

	private void OnLogin() {
		if (Config.TakeCycleKey) this.tryTakingUntil = DateTime.UtcNow.AddSeconds(20);
	}

	private void OnFramework(IFramework framework) {
		this.tickTarget = Targets.Target?.GameObjectId ?? 0;

		if (this.tryTakingUntil != DateTime.MinValue) {
			if (DateTime.UtcNow > this.tryTakingUntil) {
				this.tryTakingUntil = DateTime.MinValue;
			}
			else if (Targeting.Keybinds.Apply(out var said)) {
				this.tryTakingUntil = DateTime.MinValue;
				Log.Information($"DesTarget: {said}");
			}
		}

		this.watcher.Tick();
		this.picker.NoticeTarget();
		this.picker.Recorder.Tick();

		if (this.window.IsOpen && this.window.RankingVisible) this.picker.RefreshPreview();
	}

	private void OnCommand(string command, string args) {
		var trimmed = args.Trim();

		if (trimmed.StartsWith("probe", StringComparison.OrdinalIgnoreCase)) {
			Probe.Write(trimmed[5..].Trim());
			return;
		}

		if (trimmed.StartsWith("keybinds", StringComparison.OrdinalIgnoreCase)) {
			Keybinds.Dump();
			return;
		}

		if (trimmed.StartsWith("record", StringComparison.OrdinalIgnoreCase)) {
			var rest = trimmed[6..].Trim();

			if (rest.Equals("stop", StringComparison.OrdinalIgnoreCase)) {
				this.picker.Recorder.Stop("asked to");
				return;
			}

			this.picker.Recorder.Start(int.TryParse(rest, out var minutes) ? minutes : 360);
			return;
		}

		this.Open();
	}

	private void Open() => this.window.Toggle();

	public void Dispose() {
		this.actionHook?.Disable();
		this.actionHook?.Dispose();

		Framework.Update -= this.OnFramework;
		ClientState.Login -= this.OnLogin;
		ClientState.TerritoryChanged -= this.OnZone;
		PluginInterface.UiBuilder.Draw -= this.OnDraw;
		PluginInterface.UiBuilder.OpenConfigUi -= this.Open;
		PluginInterface.UiBuilder.OpenMainUi -= this.Open;

		this.picker.Recorder.Stop("plugin unloaded");
		Commands.RemoveHandler(CommandName);
		this.windows.RemoveAllWindows();
	}
}
