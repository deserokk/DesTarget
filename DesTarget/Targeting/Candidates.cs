using System;
using System.Collections.Generic;
using System.Numerics;

using Dalamud.Game.ClientState.Objects.Enums;
using Dalamud.Game.ClientState.Objects.Types;

using FFXIVClientStructs.FFXIV.Client.Game;
using FFXIVClientStructs.FFXIV.Client.Game.Control;

using CSGameObject = FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject;

namespace DesTarget.Targeting;

internal sealed class Candidate {
	public required IGameObject Object { get; init; }

	public ulong Id { get; init; }

	public uint BaseId { get; init; }

	public byte Kind { get; init; }

	public float Distance { get; init; }

	public float Angle { get; init; }

	public float Height { get; init; }

	public float Health { get; init; }

	public bool InViewRange { get; init; }

	public bool OnScreen { get; init; }

	public float Aim { get; set; }
	public float Near { get; set; }
	public float Red { get; set; }

	public int Group { get; set; }
	public string Note { get; set; } = string.Empty;
}

internal static class Candidates {

	internal const uint AttackAction = 142;

	internal const float MaxDistance = 50f;

	internal static unsafe bool CameraForward(out Vector2 forward) {
		forward = Vector2.Zero;

		var manager = CameraManager.Instance();
		if (manager == null) return false;

		var camera = manager->GetActiveCamera();
		if (camera == null) return false;

		var h = camera->DirH;
		forward = new Vector2(-MathF.Sin(h), -MathF.Cos(h));
		return true;
	}

	internal static unsafe List<Candidate> Gather(float? ceiling = null, bool measure = false) {
		var found = new List<Candidate>();

		var player = Plugin.Objects.LocalPlayer;
		if (player is null || !CameraForward(out var forward)) return found;

		var pvp = Plugin.ClientState.IsPvP;
		var origin = player.Position;
		var targeting = TargetSystem.Instance();

		foreach (var obj in Plugin.Objects) {
			if (obj.Address == player.Address) continue;

			var kind = obj.ObjectKind;
			if (kind != ObjectKind.BattleNpc && !(pvp && kind == ObjectKind.Pc)) continue;

			if (obj is not IBattleChara chara) continue;
			if (!obj.IsTargetable || obj.IsDead || chara.CurrentHp == 0) continue;

			var offset = obj.Position - origin;

			var limit = (ceiling ?? MaxDistance) + obj.HitboxRadius;
			if (offset.LengthSquared() > limit * limit) continue;

			var native = (CSGameObject*)obj.Address;
			if (native == null) continue;

			if (!ActionManager.CanUseActionOnTarget(AttackAction, native)) continue;

			var level = MathF.Sqrt((offset.X * offset.X) + (offset.Z * offset.Z));
			var outward = MathF.Max(0f, level - obj.HitboxRadius);
			var distance = MathF.Sqrt((outward * outward) + (offset.Y * offset.Y));

			var flat = new Vector2(offset.X, offset.Z);
			var across = flat.Length();

			var angle = across <= 0f
				? 0f
				: MathF.Acos(Math.Clamp(Vector2.Dot(flat / across, forward), -1f, 1f));

			if (obj.HitboxRadius > 0f && across > 0f) {
				var halfWidth = obj.HitboxRadius >= across ? MathF.PI : MathF.Asin(obj.HitboxRadius / across);
				angle = MathF.Max(0f, angle - halfWidth);
			}

			found.Add(new Candidate {
				Object = obj,
				Id = obj.GameObjectId,
				BaseId = obj.BaseId,
				Kind = (byte)obj.ObjectKind,
				Distance = distance,
				Height = offset.Y,
				Angle = angle,
				Health = chara.MaxHp == 0 ? 1f : (float)chara.CurrentHp / chara.MaxHp,
				InViewRange = measure && targeting != null && targeting->IsObjectInViewRange(native),
				OnScreen = measure && targeting != null && targeting->IsObjectOnScreen(native),
			});
		}

		return found;
	}
}
