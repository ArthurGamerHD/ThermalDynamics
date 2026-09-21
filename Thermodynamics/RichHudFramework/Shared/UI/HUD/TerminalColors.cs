using VRageMath;

namespace RichHudFramework.UI
{
	public static class TerminalFormatting
	{
/// <summary>GlyphFormat operation.</summary>
		public static readonly GlyphFormat HeaderFormat = new GlyphFormat(Color.White, TextAlignment.Center, 1.15f);
		public static readonly GlyphFormat ControlFormat = GlyphFormat.Blueish.WithSize(1.08f);
		public static readonly GlyphFormat InvControlFormat = ControlFormat.WithColor(Charcoal);
/// <summary>GlyphFormat operation.</summary>
		public static readonly GlyphFormat WarningFormat = new GlyphFormat(new Color(200, 55, 55));

/// <summary>Color operation.</summary>
		public static readonly Color OuterSpace = new Color(42, 55, 62);

/// <summary>Color operation.</summary>
		public static readonly Color DarkSlateGrey = new Color(41, 54, 62);

/// <summary>Color operation.</summary>
		public static readonly Color Gunmetal = new Color(39, 50, 57);

/// <summary>Color operation.</summary>
		public static readonly Color Dark = new Color(32, 39, 45);

/// <summary>Color operation.</summary>
		public static readonly Color LimedSpruce = new Color(61, 70, 78);

/// <summary>Color operation.</summary>
		public static readonly Color Atomic = new Color(60, 76, 82);

/// <summary>Color operation.</summary>
		public static readonly Color MistBlue = new Color(103, 109, 123);

/// <summary>Color operation.</summary>
		public static readonly Color StormGrey = new Color(114, 121, 136);

/// <summary>Color operation.</summary>
		public static readonly Color MidGrey = new Color(86, 93, 104);

/// <summary>Color operation.</summary>
		public static readonly Color EbonyClay = new Color(34, 44, 53);

/// <summary>Color operation.</summary>
		public static readonly Color Mercury = new Color(225, 230, 236);

/// <summary>Color operation.</summary>
		public static readonly Color BlackPerl = new Color(29, 37, 40);

/// <summary>Color operation.</summary>
		public static readonly Color Cinder = new Color(33, 41, 45);

/// <summary>Color operation.</summary>
		public static readonly Color Charcoal = new Color(39, 49, 55);

/// <summary>Color operation.</summary>
		public static readonly Color Mint = new Color(142, 188, 206);

/// <summary>Color operation.</summary>
		public static readonly Color DullMint = new Color(91, 115, 123);
	}
}
