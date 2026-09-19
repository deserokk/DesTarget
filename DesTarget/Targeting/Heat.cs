using System;

namespace DesTarget.Targeting;

internal static class Heat {

	internal static float Aim(float angleRadians, float fadeRadians) {
		if (fadeRadians <= 0f) return 0f;

		var t = angleRadians / fadeRadians;
		return MathF.Exp(-t * t);
	}

	internal static float Near(float distance, float reach, float fadeYalms) {
		if (distance <= reach) return 1f;
		if (fadeYalms <= 0f) return 0f;

		var t = (distance - reach) / fadeYalms;
		return MathF.Exp(-t * t);
	}

	internal static float Red(float aim, float near, float aimWeight) {
		var w = Math.Clamp(aimWeight, 0f, 1f);
		return (w * aim) + ((1f - w) * near);
	}
}
