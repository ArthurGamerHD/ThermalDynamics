using System;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using FontMembers = MyTuple<
		string, // Name
		int, // Index
		float, // PtSize
		float, // BaseScale
		Func<int, bool>, // IsStyleDefined
		ApiMemberAccessor
	>;

	namespace UI
	{
		namespace Rendering.Client
		{
			public sealed partial class FontManager
			{
				private class FontData : IFontMin
				{
					public string Name { get; }

					public int Index { get; }

					public float PtSize { get; }

					public float BaseScale { get; }

/// <summary>Vector2I operation.</summary>
					public Vector2I Regular => new Vector2I(Index, 0);

/// <summary>Vector2I operation.</summary>
					public Vector2I Bold => new Vector2I(Index, (int)FontStyles.Bold);

/// <summary>Vector2I operation.</summary>
					public Vector2I Italic => new Vector2I(Index, (int)FontStyles.Italic);

/// <summary>Vector2I operation.</summary>
					public Vector2I Underline => new Vector2I(Index, (int)FontStyles.Underline);

/// <summary>Vector2I operation.</summary>
					public Vector2I BoldItalic => new Vector2I(Index, (int)FontStyles.BoldItalic);

/// <summary>Vector2I operation.</summary>
					public Vector2I BoldUnderline => new Vector2I(Index, (int)(FontStyles.Bold | FontStyles.Underline));

/// <summary>Vector2I operation.</summary>
					public Vector2I BoldItalicUnderline => new Vector2I(Index, (int)(FontStyles.BoldItalic | FontStyles.Underline));

					private readonly Func<int, bool> IsFontDefinedFunc;

/// <summary>FontData operation.</summary>
					public FontData(FontMembers members)
					{
						Name = members.Item1;
						Index = members.Item2;
						PtSize = members.Item3;
						BaseScale = members.Item4;
						IsFontDefinedFunc = members.Item5;
					}

/// <summary>IsStyleDefined operation.</summary>
					public bool IsStyleDefined(FontStyles styleEnum) =>
						IsFontDefinedFunc((int)styleEnum);

/// <summary>IsStyleDefined operation.</summary>
					public bool IsStyleDefined(int style) =>
						IsFontDefinedFunc(style);

/// <summary>Returns the styleindex.</summary>
					public Vector2I GetStyleIndex(int style) =>
/// <summary>Vector2I operation.</summary>
						new Vector2I(Index, style);

/// <summary>Returns the styleindex.</summary>
					public Vector2I GetStyleIndex(FontStyles style) =>
/// <summary>Vector2I operation.</summary>
						new Vector2I(Index, (int)style);

/// <summary>Returns the hashcode.</summary>
					public override int GetHashCode()
					{
						return Index.GetHashCode();
					}

/// <summary>Equals operation.</summary>
					public override bool Equals(object obj)
					{
						var font = obj as FontData;

						return font != null && font.Index == Index;
					}
				}
			}
		}
	}
}