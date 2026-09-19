using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Bindings.ImGui;

namespace DesTarget.Targeting;

internal static class Overlay {

	private static readonly float[] Levels = [0.75f, 0.5f, 0.25f];

	private const int Steps = 48;

	private const float MaxDraw = 60f;

	internal static Tuning? Draft { get; set; }

	internal static void Draw() {
		if (!Plugin.Config.ShowPaint) return;

		var started = Cost.Begin();

		var player = Plugin.Objects.LocalPlayer;
		if (player is null || !Candidates.CameraForward(out var forward)) return;

		var tuning = Draft ?? Tuning.For(player);
		var list = ImGui.GetBackgroundDrawList();
		var origin = player.Position;

		for (var i = 0; i < Levels.Length; i++) {
			var level = Levels[i];
			var alpha = 0.75f - (i * 0.18f);
			Ring(list, origin, forward, tuning, level, Colour(1f, 0.25f, 0.2f, alpha), 2.5f - (i * 0.5f));
		}

		Ring(list, origin, forward, tuning, level: -1f, Colour(1f, 0.82f, 0.25f, 0.9f), 2f,
		     fixedDistance: tuning.Reach);

		var end = tuning.Reach + (tuning.NearFadeYalms * 2.2f);
		if (Project(origin, out var here) && Project(Point(origin, forward, 0f, MathF.Min(end, MaxDraw)), out var there))
			list.AddLine(here, there, Colour(1f, 1f, 1f, 0.55f), 1.5f);

		Cost.RecordPaint(started);
	}

	private static void Ring(ImDrawListPtr list, Vector3 origin, Vector2 forward, Tuning tuning,
	                         float level, uint colour, float thickness, float fixedDistance = -1f) {
		var run = new List<Vector2>(Steps + 1);

		for (var step = 0; step <= Steps; step++) {
			var angle = (step / (float)Steps * MathF.Tau) - MathF.PI;

			var distance = fixedDistance >= 0f ? fixedDistance : Solve(angle, tuning, level);
			if (distance < 0f || !Project(Point(origin, forward, angle, distance), out var screen)) {
				Flush(list, run, colour, thickness);
				continue;
			}

			run.Add(screen);
		}

		Flush(list, run, colour, thickness);
	}

	private static unsafe void Flush(ImDrawListPtr list, List<Vector2> run, uint colour, float thickness) {
		if (run.Count > 1) {
			var points = run.ToArray();
			fixed (Vector2* first = points)
				list.AddPolyline(first, points.Length, colour, ImDrawFlags.None, thickness);
		}

		run.Clear();
	}

	private static float Solve(float angle, Tuning tuning, float level) {

		var weight = Math.Clamp(tuning.AimWeight, 0f, 0.98f);

		var aim = Heat.Aim(MathF.Abs(angle), tuning.AimFadeRadians);
		var need = (level - (weight * aim)) / (1f - weight);

		if (need > 1f) return -1f;

		if (need <= 0f) return MaxDraw;

		var distance = tuning.Reach + (tuning.NearFadeYalms * MathF.Sqrt(-MathF.Log(need)));
		return MathF.Min(distance, MaxDraw);
	}

	private static Vector3 Point(Vector3 origin, Vector2 forward, float angle, float distance) {
		var cos = MathF.Cos(angle);
		var sin = MathF.Sin(angle);

		var dir = new Vector2((forward.X * cos) - (forward.Y * sin), (forward.X * sin) + (forward.Y * cos));
		return origin + new Vector3(dir.X * distance, 0f, dir.Y * distance);
	}

	private static bool Project(Vector3 world, out Vector2 screen)
		=> Plugin.GameGui.WorldToScreen(world, out screen);

	private static uint Colour(float r, float g, float b, float a)
		=> ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));
}
