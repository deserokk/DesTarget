using System;
using System.Collections.Generic;

using Dalamud.Configuration;

using DesTarget.Input;
using DesTarget.Targeting;

namespace DesTarget;

[Serializable]
public sealed class Configuration: IPluginConfiguration {
	public int Version { get; set; } = 3;

	public Keybind Cycle { get; set; } = new() { RepeatMs = 300 };

	public JobTuning MeleeDefaults { get; set; } = new() {
		AimFadeDegrees = 35f, Reach = 5f, NearFadeYalms = 15f, AimWeight = 0.45f,
	};

	public JobTuning RangedDefaults { get; set; } = new() {
		AimFadeDegrees = 35f, Reach = 25f, NearFadeYalms = 15f, AimWeight = 0.75f,
	};

	public float HealthTolerance { get; set; } = 0.08f;

	public float HealthTolerancePvp { get; set; } = 0.20f;

	public bool TakeCycleKey { get; set; }

	public int TakenKey { get; set; }

	public int TakenModifier { get; set; }

	public bool PickWhenNoTarget { get; set; }

	public bool IgnoreBehind { get; set; }

	public float BehindDegrees { get; set; } = 110f;

	public float RelevantFloor { get; set; } = 0.15f;

	public float BurstSeconds { get; set; } = 4f;

	public bool ShowRanking { get; set; }

	public uint UiAccentRgb { get; set; } = 0x4E8FD6;

	public Dictionary<uint, JobTuning> PerJob { get; set; } = new();

	public bool ShowPaint { get; set; }

	public float UiScale { get; set; } = 1f;

	public void Migrate() {
		if (this.Version >= 3) return;

		this.Cycle.Repeat = true;

		this.Version = 3;
		this.Save();
	}

	public void Save() => Plugin.PluginInterface.SavePluginConfig(this);
}
