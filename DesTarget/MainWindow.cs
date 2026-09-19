using System;
using System.Numerics;

using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Windowing;

using DesTarget.Input;
using DesTarget.Targeting;
using DesTarget.UI;

namespace DesTarget;

internal sealed class MainWindow: Window {
	private readonly Picker picker;

	public MainWindow(Picker picker): base("DesTarget###destarget") {
		this.picker = picker;

		this.Flags |= ImGuiWindowFlags.AlwaysAutoResize;
	}

	private static float Width => 500f * Chrome.Scale;

	public override void PreDraw() {
		Theme.Push();
		ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(Chrome.Pad, 14f * Chrome.Scale));
	}

	public override void PostDraw() {
		ImGui.PopStyleVar();
		Theme.Pop();
	}

	private bool hoodRevealed;

	public override void OnClose() {
		this.hoodRevealed = false;
		this.draft = null;
		Overlay.Draft = null;
	}

	public override void Draw() {

		KeybindWatcher.TextInputActive = ImGui.GetIO().WantTextInput;

		if (ImGui.GetIO().KeyAlt) this.hoodRevealed = true;

		if (!ImGui.BeginTabBar("##destarget_tabs")) return;

		this.DrawMain();
		this.DrawTuningPage();

		if (this.hoodRevealed) this.DrawRanking();
		else this.RankingVisible = false;

		ImGui.EndTabBar();

		this.DrawRestorePopup();
	}

	private void DrawMain() {
		if (!ImGui.BeginTabItem("Targeting")) return;

		var config = Plugin.Config;
		ImGui.Dummy(new Vector2(Width, 2f));

		this.DrawTakeover();
		ImGui.Spacing();
		DrawHotkey(config);

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();

		var autoPick = config.PickWhenNoTarget;
		if (Chrome.Toggle("autopick", ref autoPick)) {
			config.PickWhenNoTarget = autoPick;
			config.Save();
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("Auto target abilities if none are selected");

		var ignore = config.IgnoreBehind;
		if (Chrome.Toggle("behind", ref ignore)) {
			config.IgnoreBehind = ignore;
			config.Save();
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("Ignore target options behind you");

		if (ignore) {
			var degrees = config.BehindDegrees;
			ImGui.SetNextItemWidth(180f);
			if (ImGui.SliderFloat("behind starts at##behinddeg", ref degrees, 70f, 180f, "%.0f deg"))
				config.BehindDegrees = degrees;
			if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
		}

		ImGui.EndTabItem();
	}

	private static void DrawHotkey(Configuration config) {
		ImGui.AlignTextToFramePadding();
		ImGui.TextUnformatted("Target Hotkey");
		ImGui.SameLine();

		ImGui.BeginDisabled(config.TakeCycleKey);
		if (KeybindPicker.Draw("cycle", config.Cycle, repeats: false)) config.Save();
		ImGui.EndDisabled();

		if (!config.Cycle.IsBound) return;

		ImGui.SameLine();
		ImGui.Dummy(new Vector2(12f, 0f));
		ImGui.SameLine();

		var repeat = config.Cycle.Repeat;
		if (Chrome.Toggle("repeat", ref repeat)) {
			config.Cycle.Repeat = repeat;
			config.Save();
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("repeat");

		if (!repeat) return;

		ImGui.SameLine();
		var seconds = Math.Max(50, config.Cycle.RepeatMs) / 1000f;
		ImGui.SetNextItemWidth(120f);
		if (ImGui.SliderFloat("##repeatrate", ref seconds, 0.05f, 1f, "%.2f s"))
			config.Cycle.RepeatMs = (int)Math.Round(seconds * 1000f);
		if (ImGui.IsItemDeactivatedAfterEdit()) config.Save();
	}

	private void DrawTuningPage() {
		if (!ImGui.BeginTabItem("Tuning")) {
			Overlay.Draft = null;
			return;
		}

		var config = Plugin.Config;
		ImGui.Dummy(new Vector2(Width, 2f));

		var paint = config.ShowPaint;
		if (Chrome.Toggle("paint", ref paint)) {
			config.ShowPaint = paint;
			config.Save();
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("Paint the falloff on the ground");

		ImGui.Spacing();
		ImGui.Separator();

		this.DrawJobSliders();

		ImGui.Spacing();
		ImGui.Separator();
		this.DrawRestore();

		ImGui.EndTabItem();
	}

	private void DrawHoodSettings() {
		var config = Plugin.Config;

		Section("When health decides");
		Slider("Close enough (PvE)", config.HealthTolerance, 0f, 0.5f, "%.2f",
			"How close two targets can score before the lower health one goes first.",
			v => config.HealthTolerance = v);
		Slider("Close enough (PvP)", config.HealthTolerancePvp, 0f, 0.5f, "%.2f",
			"Same, in PvP.",
			v => config.HealthTolerancePvp = v);

		Section("Cycling");
		Slider("Worth cycling to above", config.RelevantFloor, 0f, 0.6f, "%.2f",
			"Targets scoring under this are skipped while anything better is around.",
			v => config.RelevantFloor = v);
		Slider("A run of presses lasts", config.BurstSeconds, 1f, 10f, "%.0fs",
			"Presses closer together than this keep walking the list. After a pause, it starts over.",
			v => config.BurstSeconds = v);

		ImGui.Spacing();
		this.DrawRecording();

		ImGui.Spacing();
		ImGui.Separator();
	}

	private void DrawRecording() {
		var recorder = this.picker.Recorder;

		var on = recorder.Running;
		if (Chrome.Toggle("debuglog", ref on)) {
			if (on) recorder.Start(360);
			else recorder.Stop("asked to");
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("Debug logging");
		ImGui.SameLine();
		ImGui.TextColored(Theme.Faint, "(?)");
		if (ImGui.IsItemHovered()) ImGui.SetTooltip("Do not press unless you know what you're doing");
	}

	private void DrawTakeover() {
		var take = Plugin.Config.TakeCycleKey;

		if (Chrome.Toggle("takeover", ref take)) {
			Plugin.Config.TakeCycleKey = take;
			Plugin.Config.Save();

			if (take) Keybinds.Apply(out _);
			else Keybinds.GiveBack();
		}

		ImGui.SameLine();
		ImGui.TextUnformatted("Use the game's targetting hotkey");
	}

	private bool askRestore;

	internal bool RankingVisible { get; private set; }

	private void DrawRestore() {
		if (Accent.Button("Restore defaults", Accent.Amber)) this.askRestore = true;
	}

	private void DrawRestorePopup() {
		const string Title = "Restore defaults?";

		if (this.askRestore) {
			ImGui.OpenPopup(Title);
			this.askRestore = false;
		}

		var viewport = ImGui.GetMainViewport();
		ImGui.SetNextWindowPos(viewport.Pos + (viewport.Size * 0.5f), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));

		if (!ImGui.BeginPopupModal(Title, ImGuiWindowFlags.AlwaysAutoResize)) return;

		var jobs = Plugin.Config.PerJob.Count;

		if (jobs > 0)
			ImGui.TextColored(Theme.Negative, $"This will clear {jobs} job{(jobs == 1 ? "" : "s")} you have tuned.");

		ImGui.TextUnformatted("Your keybind is preserved.");

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.Spacing();

		if (ImGui.Button("Cancel", new Vector2(120f, 0f))) ImGui.CloseCurrentPopup();

		ImGui.SameLine();
		ImGui.Dummy(new Vector2(20f, 0f));
		ImGui.SameLine();

		if (Accent.Button("Restore everything", Accent.Amber, new Vector2(160f, 0f))) {
			Restore();
			this.draft = null;
			ImGui.CloseCurrentPopup();
		}

		ImGui.EndPopup();
	}

	private static void Restore() {
		var fresh = new Configuration();
		var config = Plugin.Config;

		config.MeleeDefaults = fresh.MeleeDefaults;
		config.RangedDefaults = fresh.RangedDefaults;
		config.HealthTolerance = fresh.HealthTolerance;
		config.HealthTolerancePvp = fresh.HealthTolerancePvp;
		config.RelevantFloor = fresh.RelevantFloor;
		config.BurstSeconds = fresh.BurstSeconds;
		config.PerJob.Clear();
		Overlay.Draft = null;

		config.Save();
	}

	private Tuning? draft;

	private uint draftJob;

	private void DrawJobSliders() {
		var player = Plugin.Objects.LocalPlayer;
		if (player is null) {
			Overlay.Draft = null;
			ImGui.Spacing();
			ImGui.TextColored(Theme.Faint, "Log in to set up a job.");
			return;
		}

		var jobId = player.ClassJob.RowId;
		var jobName = player.ClassJob.ValueNullable?.Name.ExtractText() ?? "this job";
		jobName = jobName.Length == 0 ? "this job" : char.ToUpperInvariant(jobName[0]) + jobName[1..];

		var saved = Tuning.For(player);
		var group = saved.Melee ? "melee" : "ranged";

		if (this.draft is null || this.draftJob != jobId) {
			this.draft = saved;
			this.draftJob = jobId;
		}

		var d = this.draft.Value;
		var dirty = d != saved;
		Overlay.Draft = dirty ? d : null;

		ImGui.Spacing();
		ImGui.TextColored(Theme.Faint, saved.Overridden
			? $"{jobName} is using its own settings."
			: $"{jobName} is using the {group} settings.");

		Section("Targeting Configuration");

		Action<Tuning> edit = t => this.draft = t;

		Knob("Looseness", d.AimFadeDegrees, 10f, 90f, "%.0f deg",
			"Priority drop off from screen center. Lower means more precision, higher means more targets considered.",
			d, (t, v) => t with { AimFadeDegrees = v }, edit);

		Knob("Reach", d.Reach, 2f, 30f, "%.0fy",
			"All targets within this range count as equally close. Set it to your attack range.",
			d, (t, v) => t with { Reach = v }, edit);

		Knob("Distance Drop off", d.NearFadeYalms, 3f, 40f, "%.0fy",
			"How fast far away targets lose priority. Lower only prioritizes close targets. "
			+ "Gap closers are around 15y to 20y.",
			d, (t, v) => t with { NearFadeYalms = v }, edit);

		Knob("Aim vs. Closeness Ratio", d.AimWeight, 0f, 1f, "%.2f",
			"Left means close targets win, right prefers targets you have centered.",
			d, (t, v) => t with { AimWeight = v }, edit);

		ImGui.Spacing();

		if (Accent.Button($"Save for all {group} jobs", Accent.Blue)) {
			d.SaveForGroup();
			Plugin.Config.PerJob.Remove(jobId);
			Plugin.Config.Save();
			this.draft = null;
		}

		ImGui.SameLine();

		if (Accent.Button($"Save for {jobName}", Accent.Blue)) {
			d.SaveFor(jobId);
			this.draft = null;
		}

		if (dirty) {
			ImGui.SameLine();
			ImGui.TextColored(Theme.Faint, "not saved yet");
		}
	}

	private static void Knob(string label, float value, float min, float max, string format, string help,
	                         Tuning tuning, Func<Tuning, float, Tuning> apply, Action<Tuning> changed) {
		var working = value;

		ImGui.SetNextItemWidth(220f);
		if (ImGui.SliderFloat($"{label}##{label}", ref working, min, max, format))
			changed(apply(tuning, Math.Clamp(working, min, max)));

		ImGui.SameLine();
		ImGui.TextColored(Theme.Faint, "(?)");
		if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
	}

	private void DrawRanking() {
		if (!ImGui.BeginTabItem("Under the Hood")) {
			this.RankingVisible = false;
			return;
		}

		this.RankingVisible = true;
		ImGui.Dummy(new Vector2(Width, 2f));
		this.DrawHoodSettings();

		ImGui.Spacing();
		if (this.picker.LastSummary.Length > 0) ImGui.TextColored(Theme.Dim, this.picker.LastSummary);
		ImGui.Spacing();

		var order = this.picker.Preview;
		if (order.Count == 0) {
			ImGui.TextColored(Theme.Faint, "No Targets nearby");
			ImGui.EndTabItem();
			return;
		}

		const ImGuiTableFlags Flags = ImGuiTableFlags.RowBg | ImGuiTableFlags.Borders
			| ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY;

		var rows = ImGui.GetTextLineHeightWithSpacing() * 12f;

		if (ImGui.BeginTable("##ranking", 9, Flags, new Vector2(Width, rows))) {
			ImGui.TableSetupScrollFreeze(0, 1);
			ImGui.TableSetupColumn("#", ImGuiTableColumnFlags.WidthFixed, 26f);
			ImGui.TableSetupColumn("grp", ImGuiTableColumnFlags.WidthFixed, 28f);
			ImGui.TableSetupColumn("target", ImGuiTableColumnFlags.WidthStretch, 2.4f);
			ImGui.TableSetupColumn("red");
			ImGui.TableSetupColumn("aim");
			ImGui.TableSetupColumn("near");
			ImGui.TableSetupColumn("dist");
			ImGui.TableSetupColumn("off");
			ImGui.TableSetupColumn("hp");
			ImGui.TableHeadersRow();

			var currentId = Plugin.Targets.Target?.GameObjectId ?? 0;

			for (var i = 0; i < order.Count; i++) {
				var c = order[i];
				ImGui.TableNextRow();

				ImGui.TableNextColumn();
				ImGui.TextColored(i == 0 ? Theme.Accent : Theme.Faint, (i + 1).ToString());

				ImGui.TableNextColumn();
				ImGui.TextColored(Theme.Faint, c.Group.ToString());

				ImGui.TableNextColumn();
				var name = c.Object.Name.TextValue;
				ImGui.TextColored(c.Id == currentId ? Theme.Positive : Theme.Ink,
					name.Length == 0 ? "(unnamed)" : name);

				if (c.Note.Length > 0) {
					ImGui.SameLine();
					ImGui.TextColored(Theme.Faint, $"({c.Note})");
				}

				Cell($"{c.Red:0.00}", i == 0);
				Cell($"{c.Aim:0.00}", false);
				Cell($"{c.Near:0.00}", false);
				Cell($"{c.Distance:0.#}y", false);
				Cell($"{c.Angle * 180f / MathF.PI:0}", false);
				Cell($"{c.Health * 100f:0}%", false);
			}

			ImGui.EndTable();
		}

		this.DrawBurst();
		DrawCost();
		ImGui.EndTabItem();
	}

	private void DrawBurst() {
		var since = this.picker.LastActionAt == DateTime.MinValue
			? "none yet"
			: $"{(DateTime.UtcNow - this.picker.LastActionAt).TotalSeconds:0.0}s ago";

		ImGui.Spacing();
		ImGui.Separator();
		ImGui.TextColored(Theme.Faint, "CYCLE");
		ImGui.TextColored(Theme.Dim,
			$"walked {this.picker.Walked}   |   abilities {this.picker.ActionsSeen}, last {since}");
	}

	private static void DrawCost() {
		ImGui.Spacing();
		ImGui.Separator();
		ImGui.TextColored(Theme.Faint, "COST, MICROSECONDS (A FRAME IS 6900 AT 144FPS)");

		ImGui.TextColored(Theme.Dim,
			$"press: {Cost.Press:0}, worst {Cost.PressWorst:0}, {Cost.Candidates} candidates");

		ImGui.TextColored(Theme.Dim, $"this table: {Cost.Scan:0}");

		ImGui.TextColored(Cost.AutoPicks > 0 ? Theme.Dim : Theme.Faint, Cost.AutoPicks > 0
			? $"auto target: {Cost.AutoPick:0} worst, {Cost.AutoPicks} times"
			: "auto target: unused");

		ImGui.TextColored(Plugin.Config.ShowPaint ? Theme.Dim : Theme.Faint,
			Plugin.Config.ShowPaint
				? $"paint: {Cost.Paint:0}"
				: "paint: off");
	}

	private static void Cell(string text, bool lead) {
		ImGui.TableNextColumn();
		ImGui.TextColored(lead ? Theme.Ink : Theme.Dim, text);
	}

	private static void Section(string label) {
		ImGui.Spacing();
		ImGui.TextColored(Theme.Faint, label.ToUpperInvariant());
		ImGui.Spacing();
	}

	private static void Slider(string label, float value, float min, float max, string format,
	                           string help, Action<float> set) {
		var working = value;

		ImGui.SetNextItemWidth(220f);
		if (ImGui.SliderFloat($"{label}##{label}", ref working, min, max, format))
			set(Math.Clamp(working, min, max));

		if (ImGui.IsItemDeactivatedAfterEdit()) Plugin.Config.Save();

		ImGui.SameLine();
		ImGui.TextColored(Theme.Faint, "(?)");
		if (ImGui.IsItemHovered()) ImGui.SetTooltip(help);
	}
}
