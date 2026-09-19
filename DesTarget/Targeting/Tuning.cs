using System;
using System.Collections.Generic;

using Dalamud.Game.ClientState.Objects.SubKinds;

namespace DesTarget.Targeting;

[Serializable]
public sealed class JobTuning {
	public float AimFadeDegrees { get; set; }
	public float Reach { get; set; }
	public float NearFadeYalms { get; set; }
	public float AimWeight { get; set; }
}

internal readonly record struct Tuning(float AimFadeDegrees, float Reach, float NearFadeYalms,
                                       float AimWeight, bool Melee, bool Overridden) {
	internal float AimFadeRadians => MathF.Max(0.01f, this.AimFadeDegrees * MathF.PI / 180f);

	internal static Tuning RoleDefault(bool melee) {
		var set = melee ? Plugin.Config.MeleeDefaults : Plugin.Config.RangedDefaults;
		return new Tuning(set.AimFadeDegrees, set.Reach, set.NearFadeYalms, set.AimWeight, melee, Overridden: false);
	}

	internal static bool IsMelee(IPlayerCharacter player)
		=> (player.ClassJob.ValueNullable?.Role ?? 0) is 1 or 2;

	internal static Tuning For(IPlayerCharacter player) {
		var melee = IsMelee(player);
		var jobId = player.ClassJob.RowId;

		if (!Plugin.Config.PerJob.TryGetValue(jobId, out var own) || own is null)
			return RoleDefault(melee);

		return new Tuning(own.AimFadeDegrees, own.Reach, own.NearFadeYalms, own.AimWeight, melee,
		                  Overridden: true);
	}

	private JobTuning ToJob() => new() {
		AimFadeDegrees = this.AimFadeDegrees,
		Reach = this.Reach,
		NearFadeYalms = this.NearFadeYalms,
		AimWeight = this.AimWeight,
	};

	internal void SaveFor(uint jobId) {
		Plugin.Config.PerJob[jobId] = this.ToJob();
		Plugin.Config.Save();
	}

	internal void SaveForGroup() {
		if (this.Melee) Plugin.Config.MeleeDefaults = this.ToJob();
		else Plugin.Config.RangedDefaults = this.ToJob();

		Plugin.Config.Save();
	}
}
