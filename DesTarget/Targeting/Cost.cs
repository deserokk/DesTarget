using System.Diagnostics;

namespace DesTarget.Targeting;

internal static class Cost {

	internal static double Press { get; private set; }

	internal static double PressWorst { get; private set; }

	internal static int Candidates { get; private set; }

	internal static double Paint { get; private set; }

	internal static double Scan { get; private set; }

	internal static double AutoPick { get; private set; }

	internal static int AutoPicks { get; private set; }

	internal static void RecordAutoPick(long from) {
		var micros = Micros(from);

		AutoPicks++;
		if (micros > AutoPick) AutoPick = micros;
	}

	internal static long Begin() => Stopwatch.GetTimestamp();

	internal static double Micros(long from)
		=> (Stopwatch.GetTimestamp() - from) * 1_000_000d / Stopwatch.Frequency;

	internal static void RecordPress(long from, int candidates) {
		Press = Micros(from);
		Candidates = candidates;
		if (Press > PressWorst) PressWorst = Press;
	}

	internal static void RecordPaint(long from) => Paint = Blend(Paint, Micros(from));

	internal static void RecordScan(long from) => Scan = Blend(Scan, Micros(from));

	private static double Blend(double average, double sample)
		=> average <= 0d ? sample : (average * 0.9d) + (sample * 0.1d);
}
