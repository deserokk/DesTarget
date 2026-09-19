using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

using Dalamud.Game.ClientState.Objects.Types;

namespace DesTarget.Targeting;

internal sealed class Recorder {
	private const int MaxLines = 20000;

	private readonly StringBuilder buffer = new();
	private readonly object gate = new();

	private DateTime until = DateTime.MinValue;
	private DateTime startedAt;
	private DateTime nextFlush = DateTime.MinValue;
	private int lines;

	internal bool Running => DateTime.UtcNow < this.until;

	internal string Path
		=> System.IO.Path.Combine(Plugin.PluginInterface.ConfigDirectory.FullName, "record.log");

	internal string Status => this.Running
		? $"recording, {(this.until - DateTime.UtcNow).TotalMinutes:0.#} min left, {this.lines} lines"
		: this.lines > 0 ? $"stopped, {this.lines} lines written" : "not recording";

	internal void Start(int minutes) {
		lock (this.gate) {
			this.startedAt = DateTime.UtcNow;

			this.until = this.startedAt.AddMinutes(Math.Clamp(minutes, 1, 480));
			this.lines = 0;
			this.buffer.Clear();

			this.Line($"=== started {DateTime.Now:yyyy-MM-dd HH:mm:ss}, up to {minutes} min, zone "
				+ $"{Plugin.ClientState.TerritoryType}, pvp {Plugin.ClientState.IsPvP} ===");
			this.Line(Settings());
		}

		Plugin.Chat.Print("[DesTarget] debug logging on.");
	}

	internal void Stop(string why) {
		lock (this.gate) {
			if (this.lines == 0) return;

			this.until = DateTime.MinValue;
			this.Line($"=== stopped: {why} ===");
		}

		this.Flush();
		Plugin.Chat.Print($"[DesTarget] debug logging off, {this.lines} lines.");
	}

	private static string Settings() {
		var config = Plugin.Config;
		static string Set(JobTuning t) => $"{t.AimFadeDegrees:0}deg/{t.Reach:0.#}y/{t.NearFadeYalms:0.#}y/{t.AimWeight:0.00}";

		return $"settings: melee {Set(config.MeleeDefaults)}, ranged {Set(config.RangedDefaults)}, "
			+ $"own settings on {config.PerJob.Count} jobs, floor {config.RelevantFloor:0.00}, "
			+ $"tol {config.HealthTolerance:0.00}/{config.HealthTolerancePvp:0.00}, burst {config.BurstSeconds:0.#}s, "
			+ $"ignoreBehind {config.IgnoreBehind}";
	}

	internal void Press(IReadOnlyList<Candidate> order, Candidate? taken, int refused, double micros, int walked) {
		if (!this.Running) return;

		lock (this.gate) {
			this.Line($"[{this.Clock()}] PRESS {order.Count} candidates, walked {walked}, {micros:0}us"
				+ (refused > 0 ? $", {refused} refused" : string.Empty));

			var shown = Math.Min(order.Count, 8);
			for (var i = 0; i < shown; i++) {
				var c = order[i];
				var mark = taken is not null && c.Id == taken.Id ? ">" : " ";

				this.Line(string.Format(CultureInfo.InvariantCulture,
					"  {0}{1,2} {2,-14} red {3:0.00}  aim {4:0.00}  near {5:0.00}  {6,5:0.0}y {7,4:0}deg {8,6:+0.0;-0.0}up  hp {9,3:0}%  {10}",
					mark, i + 1, Who(c.Object), c.Red, c.Aim, c.Near, c.Distance,
					c.Angle * 180f / MathF.PI, c.Height, c.Health * 100f, c.Note));
			}

			if (order.Count > shown) this.Line($"   .. and {order.Count - shown} more");
		}
	}

	internal void Zone(uint territory) {
		if (!this.Running) return;
		lock (this.gate) this.Line($"[{this.Clock()}] --- zone {territory}, pvp {Plugin.ClientState.IsPvP} ---");
	}

	internal void Action(int walked) {
		if (!this.Running) return;

		lock (this.gate) this.Line(walked == 0
			? $"[{this.Clock()}] FIRED on a target already held"
			: $"[{this.Clock()}] FIRED after {walked} press{(walked == 1 ? string.Empty : "es")} this burst");
	}

	internal void Hooked(string name, uint id, int type, ulong targetIn, ulong targetOut,
	                     bool hadTarget, bool needsEnemy) {

		if (!this.Running || targetOut == targetIn) return;

		lock (this.gate) {
			var acted = $" -> we set {targetOut:x}";

			this.Line($"[{this.Clock()}] HOOK {name} id {id} type {type}, targetArg {targetIn:x}, "
				+ $"hadTarget {hadTarget}, needsEnemy {needsEnemy}{acted}");
		}
	}

	internal void Manual(IGameObject? now, int rankedAt, int outOf, double sincePress) {
		if (!this.Running) return;

		lock (this.gate) {
			var where = rankedAt > 0 ? $"our slot {rankedAt} of {outOf}" : $"not a candidate at all (of {outOf})";

			var kind = sincePress <= 2d ? "CORRECTION" : "manual";

			this.Line($"[{this.Clock()}] {kind} -> {Who(now)} ({where}), {sincePress:0.0}s after the last press");
		}
	}

	internal void Tick() {
		if (!this.Running) {
			if (this.buffer.Length > 0) this.Flush();
			return;
		}

		if (this.lines >= MaxLines) {
			this.Stop($"hit the {MaxLines} line cap");
			return;
		}

		var now = DateTime.UtcNow;
		if (now < this.nextFlush) return;

		this.nextFlush = now.AddSeconds(5);
		this.Flush();
	}

	private void Line(string text) {
		this.buffer.AppendLine(text);
		this.lines++;
	}

	private string Clock() => $"{(DateTime.UtcNow - this.startedAt).TotalSeconds,6:0.0}";

	private void Flush() {
		string text;

		lock (this.gate) {
			if (this.buffer.Length == 0) return;

			text = this.buffer.ToString();
			this.buffer.Clear();
		}

		try {
			Directory.CreateDirectory(Plugin.PluginInterface.ConfigDirectory.FullName);
			File.AppendAllText(this.Path, text);
		}
		catch (Exception ex) {
			Plugin.Log.Error(ex, "DesTarget: could not write record.log.");
		}
	}

	private static string Who(IGameObject? obj) {
		if (obj is null) return "nothing";

		if (obj is Dalamud.Game.ClientState.Objects.Types.IBattleChara chara
			&& obj.ObjectKind == Dalamud.Game.ClientState.Objects.Enums.ObjectKind.Pc) {
			var job = chara.ClassJob.ValueNullable?.Abbreviation.ExtractText() ?? "???";
			return $"{job} {obj.GameObjectId & 0xFFFF:x4}";
		}

		return $"npc {obj.BaseId}";
	}
}
