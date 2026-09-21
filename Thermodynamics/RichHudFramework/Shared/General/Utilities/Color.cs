using System;
using System.Text.RegularExpressions;

namespace RichHudFramework
{
	public static partial class Utils
	{
		public static class Color
		{
/// <summary>Regex operation.</summary>
			private static readonly Regex colorParser = new Regex(@"(\s*,?(\d{1,3})\s*,?){3,4}");

/// <summary>CanParseColor operation.</summary>
			public static bool CanParseColor(string colorData)
			{
				if (string.IsNullOrEmpty(colorData))
					return false;

				Match match = colorParser.Match(colorData);
				CaptureCollection captures = match.Groups[2].Captures;

				if (captures.Count < 3)
					return false;

				for (int i = 0; i < Math.Min(4, captures.Count); i++)
				{
					byte value;

					if (!byte.TryParse(captures[i].Value, out value))
						return false;
				}

				return true;
			}

/// <summary>TryParseColor operation.</summary>
			public static bool TryParseColor(string colorData, out VRageMath.Color value, bool ignoreAlpha = false)
			{
				try
				{
/// <summary>ParseColor operation.</summary>
					value = ParseColor(colorData, ignoreAlpha);
					return true;
				}
				catch
				{
					value = VRageMath.Color.White;
					return false;
				}
			}

/// <summary>ParseColor operation.</summary>
			public static VRageMath.Color ParseColor(string colorData, bool ignoreAlpha = false)
			{
				if (string.IsNullOrEmpty(colorData))
					throw new ArgumentException("Color string cannot be null or empty.");

				Match match = colorParser.Match(colorData);
				CaptureCollection captures = match.Groups[2].Captures;

				if (captures.Count < 3)
					throw new Exception("Color string must contain at least 3 values (R,G,B).");

				VRageMath.Color value = new VRageMath.Color
				{
					R = byte.Parse(captures[0].Value),
					G = byte.Parse(captures[1].Value),
					B = byte.Parse(captures[2].Value)
				};

				if (captures.Count > 3)
					value.A = byte.Parse(captures[3].Value);
/// <summary>if operation.</summary>
				else if (!ignoreAlpha)
					value.A = 255; // default opaque when alpha omitted

				return value;
			}

/// <summary>Returns the colorstring.</summary>
			public static string GetColorString(VRageMath.Color color, bool includeAlpha = true)
			{
				return includeAlpha
					? $"{color.R},{color.G},{color.B},{color.A}"
					: $"{color.R},{color.G},{color.B}";
			}
		}
	}
}