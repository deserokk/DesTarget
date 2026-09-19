using System;
using System.Collections.Generic;
using System.Linq;

using Dalamud.Game.ClientState.Objects.SubKinds;

using Dalamud.Game.ClientState.Objects.Types;

namespace DesTarget.Targeting;

internal sealed class Picker {

	private readonly HashSet<ulong> visited = new();

	private DateTime lastPress = DateTime.MinValue;

	internal readonly Recorder Recorder = new();

	private ulong ours;

	internal IReadOnlyList<Candidate> LastOrder { get; private set; } = Array.Empty<Candidate>();

	internal string LastSummary { get; private set; } = string.Empty;

	internal int ActionsSeen { get; private set; }

	internal DateTime LastActionAt { get; private set; } = DateTime.MinValue;

	internal int Walked => this.visited.Count;

	internal void NoticeAction() {

		this.Recorder.Action(this.visited.Count);
		this.ActionsSeen++;
		this.LastActionAt = DateTime.UtcNow;
		this.visited.Clear();
	}

	internal List<Candidate> Order(List<Candidate> candidates, IReadOnlySet<ulong> seen, ulong currentId) {
		var player = Plugin.Objects.LocalPlayer;
		if (player is null || candidates.Count == 0) return candidates;

		var tuning = Tuning.For(player);

		foreach (var c in candidates) {
			c.Aim = Heat.Aim(c.Angle, tuning.AimFadeRadians);
			c.Near = Heat.Near(c.Distance, tuning.Reach, tuning.NearFadeYalms);
			c.Red = Heat.Red(c.Aim, c.Near, tuning.AimWeight);
			c.Note = string.Empty;
		}

		if (Plugin.Config.IgnoreBehind) {
			var behind = Plugin.Config.BehindDegrees * MathF.PI / 180f;
			candidates = candidates.Where(c => c.Angle <= behind || c.Distance <= tuning.Reach).ToList();

			if (candidates.Count == 0) return candidates;
		}

		var byRed = candidates.OrderByDescending(c => c.Red).ToList();

		var tolerance = Plugin.ClientState.IsPvP ? Plugin.Config.HealthTolerancePvp : Plugin.Config.HealthTolerance;
		var ranked = new List<Candidate>(byRed.Count);
		var groupNumber = 0;

		for (var i = 0; i < byRed.Count;) {
			groupNumber++;
			var brightest = byRed[i].Red;

			var floor = brightest * (1f - tolerance);

			var end = i;
			while (end < byRed.Count && byRed[end].Red >= floor) end++;

			var group = byRed.GetRange(i, end - i);

			if (group.Count > 1 && group.Exists(c => MathF.Abs(c.Health - group[0].Health) > 0.001f)) {

				group = group.OrderBy(c => c.Health)
				             .ThenByDescending(c => c.Red)
				             .ThenBy(c => c.Id)
				             .ToList();

				foreach (var c in group) c.Note = "health sorted";
			}

			foreach (var c in group) c.Group = groupNumber;

			ranked.AddRange(group);
			i = end;
		}

		var worthIt = Plugin.Config.RelevantFloor;

		var relevant = ranked.Where(c => c.Red >= worthIt).ToList();
		var rest = ranked.Where(c => c.Red < worthIt).ToList();

		foreach (var c in rest)
			c.Note = string.IsNullOrEmpty(c.Note) ? "below the floor" : c.Note + ", below the floor";

		if (seen.Count > 0) {
			var fresh = relevant.Where(c => !seen.Contains(c.Id)).ToList();

			foreach (var c in relevant.Where(c => seen.Contains(c.Id))) {
				c.Note = string.IsNullOrEmpty(c.Note) ? "stepped away" : c.Note + ", stepped away";
				fresh.Add(c);
			}

			relevant = fresh;
		}

		if (relevant.Count > 1 && currentId != 0) Demote(relevant, currentId);
		else if (relevant.Count == 0 && rest.Count > 1 && currentId != 0) Demote(rest, currentId);

		ranked = relevant;
		ranked.AddRange(rest);

		return ranked;
	}

	internal ulong PickForAction() {
		var started = Cost.Begin();

		var candidates = Candidates.Gather();
		if (candidates.Count == 0) return 0;

		var order = this.Order(candidates, this.visited, currentId: 0);
		Cost.RecordAutoPick(started);

		if (order.Count == 0 || order[0].Red < Plugin.Config.RelevantFloor) return 0;

		Plugin.Targets.Target = order[0].Object;
		this.ours = order[0].Id;

		return order[0].Id;
	}

	private static void Demote(List<Candidate> group, ulong currentId) {
		var at = group.FindIndex(c => c.Id == currentId);
		if (at < 0) return;

		var held = group[at];
		held.Note = string.IsNullOrEmpty(held.Note) ? "current" : held.Note + ", current";

		group.RemoveAt(at);
		group.Add(held);
	}

	internal void Next() {
		var started = Cost.Begin();
		var now = DateTime.UtcNow;

		if ((now - this.lastPress).TotalSeconds > Plugin.Config.BurstSeconds) this.visited.Clear();
		this.lastPress = now;

		var candidates = Candidates.Gather();
		if (candidates.Count == 0) {
			this.LastOrder = Array.Empty<Candidate>();
			this.LastSummary = "No Targets nearby";
			Cost.RecordPress(started, 0);
			return;
		}

		var currentId = Plugin.Targets.Target?.GameObjectId ?? 0;
		var order = this.Order(candidates, this.visited, currentId);

		var wasOn = Plugin.Targets.Target;
		Candidate? taken = null;
		var refused = 0;

		foreach (var c in order) {
			Plugin.Targets.Target = c.Object;

			if ((Plugin.Targets.Target?.GameObjectId ?? 0) == c.Id) {
				taken = c;
				break;
			}

			c.Note = string.IsNullOrEmpty(c.Note) ? "refused" : c.Note + ", refused";
			refused++;

			if (refused >= 8) break;
		}

		if (taken is null) {

			Plugin.Targets.Target = wasOn;

			this.LastOrder = order;
			this.LastSummary = $"last press: {refused} refused, nothing taken";
			Cost.RecordPress(started, candidates.Count);
			this.Recorder.Press(order, null, refused, Cost.Press, this.visited.Count);
			return;
		}

		this.visited.Add(taken.Id);
		this.ours = taken.Id;

		var worthWalking = order.Count(c => c.Red >= Plugin.Config.RelevantFloor);
		if (worthWalking > 0 && this.visited.Count >= worthWalking) this.visited.Clear();

		this.LastOrder = order;
		this.LastSummary = refused == 0
			? $"last press: slot 1 of {order.Count}"
			: $"last press: slot {refused + 1} of {order.Count}, {refused} refused";

		Cost.RecordPress(started, candidates.Count);
		this.Recorder.Press(order, taken, refused, Cost.Press, this.visited.Count);
	}

	private List<Candidate> preview = new();
	private DateTime nextPreview = DateTime.MinValue;

	internal void NoticeTarget() {
		if (!this.Recorder.Running) return;

		var now = Plugin.Targets.Target;
		var id = now?.GameObjectId ?? 0;
		if (id == this.ours) return;

		this.ours = id;
		if (id == 0) return;

		var fresh = this.Order(Candidates.Gather(), this.visited, 0);

		var rankedAt = 0;
		for (var i = 0; i < fresh.Count; i++) {
			if (fresh[i].Id != id) continue;

			rankedAt = i + 1;
			break;
		}

		this.Recorder.Manual(now, rankedAt, fresh.Count, (DateTime.UtcNow - this.lastPress).TotalSeconds);
	}

	internal void RefreshPreview() {
		var now = DateTime.UtcNow;
		if (now < this.nextPreview) return;
		this.nextPreview = now.AddMilliseconds(100);

		var started = Cost.Begin();

		var candidates = Candidates.Gather();
		this.preview = candidates.Count == 0
			? candidates
			: this.Order(candidates, this.visited, Plugin.Targets.Target?.GameObjectId ?? 0);

		Cost.RecordScan(started);
	}

	internal IReadOnlyList<Candidate> Preview => this.preview;
}
