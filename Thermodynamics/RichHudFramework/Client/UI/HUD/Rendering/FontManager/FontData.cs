using System;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;

namespace RichHudFramework
{
	using FontMembers = MyTuple<
		string,
		int,
		float,
		float,
		Func<int, bool>,
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


					public Vector2I Regular => new Vector2I(Index, 0);


					public Vector2I Bold => new Vector2I(Index, (int)FontStyles.Bold);


					public Vector2I Italic => new Vector2I(Index, (int)FontStyles.Italic);


					public Vector2I Underline => new Vector2I(Index, (int)FontStyles.Underline);


					public Vector2I BoldItalic => new Vector2I(Index, (int)FontStyles.BoldItalic);


					public Vector2I BoldUnderline => new Vector2I(Index, (int)(FontStyles.Bold | FontStyles.Underline));


					public Vector2I BoldItalicUnderline => new Vector2I(Index, (int)(FontStyles.BoldItalic | FontStyles.Underline));

					private readonly Func<int, bool> IsFontDefinedFunc;


					public FontData(FontMembers members)
					{
						Name = members.Item1;
						Index = members.Item2;
						PtSize = members.Item3;
						BaseScale = members.Item4;
						IsFontDefinedFunc = members.Item5;
					}


					public bool IsStyleDefined(FontStyles styleEnum) =>
						IsFontDefinedFunc((int)styleEnum);


					public bool IsStyleDefined(int style) =>
						IsFontDefinedFunc(style);


					public Vector2I GetStyleIndex(int style) =>

						new Vector2I(Index, style);


					public Vector2I GetStyleIndex(FontStyles style) =>

						new Vector2I(Index, (int)style);


					public override int GetHashCode()
					{
						return Index.GetHashCode();
					}


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