using System;
using System.Collections.Generic;
using VRage;
using VRageMath;
using ApiMemberAccessor = System.Func<object, int, object>;
using AtlasMembers = VRage.MyTuple<string, VRageMath.Vector2>;
using GlyphMembers = VRage.MyTuple<int, VRageMath.Vector2, VRageMath.Vector2, float, float>;

namespace RichHudFramework
{
	using Client;
	using FontMembers = MyTuple<
		string, // Name
		int, // Index
		float, // PtSize
		float, // BaseScale
		Func<int, bool>, // IsStyleDefined
		ApiMemberAccessor
	>;
	using FontStyleDefinition = MyTuple<
		int, // styleID
		float, // height
		float, // baseline
		AtlasMembers[], // atlases
		KeyValuePair<char, GlyphMembers>[], // glyphs
		KeyValuePair<uint, float>[] // kernings
	>;

	namespace UI
	{
		using FontDefinition = MyTuple<
			string, // Name
			float, // PtSize
			FontStyleDefinition[] // styles
		>;

		namespace Rendering.Client
		{
			using FontManagerMembers = MyTuple<
				MyTuple<Func<int, FontMembers>, Func<int>>, // Font List
				Func<FontDefinition, FontMembers?>, // TryAddFont
				Func<string, FontMembers?>, // GetFont
				ApiMemberAccessor
			>;

			public sealed partial class FontManager : RichHudClient.ApiModule
			{
				public static Vector2I Default => Vector2I.Zero;

				public static IReadOnlyList<IFontMin> Fonts => Instance.fonts;

				private static FontManager Instance
				{
/// <summary>Init operation.</summary>
					get { Init(); return instance; }
					set { instance = value; }
				}
				private static FontManager instance;

				private readonly ReadOnlyApiCollection<IFontMin> fonts;
				private readonly Func<FontDefinition, FontMembers?> TryAddFontFunc;
				private readonly Func<string, FontMembers?> GetFontFunc;

/// <summary>FontManager operation.</summary>
				private FontManager() : base(ApiModuleTypes.FontManager, false, true)
				{
					var members = (FontManagerMembers)GetApiData();

/// <summary>FontData operation.</summary>
					Func<int, IFontMin> fontGetter = x => new FontData(members.Item1.Item1(x));
/// <summary>ReadOnlyApiCollection operation.</summary>
					fonts = new ReadOnlyApiCollection<IFontMin>(fontGetter, members.Item1.Item2);

					TryAddFontFunc = members.Item2;
					GetFontFunc = members.Item3;
				}

/// <summary>Init operation.</summary>
				private static void Init()
				{
					if (instance == null)
/// <summary>FontManager operation.</summary>
						instance = new FontManager();
				}

/// <summary>Close operation.</summary>
				public override void Close()
				{
					instance = null;
				}

/// <summary>TryAddFont operation.</summary>
				public static bool TryAddFont(FontDefinition fontData) =>
					Instance.TryAddFontFunc(fontData) != null;

/// <summary>TryAddFont operation.</summary>
				public static bool TryAddFont(FontDefinition fontData, out IFontMin font)
				{
					FontMembers? members = Instance.TryAddFontFunc(fontData);

					if (members != null)
					{
						font = Instance.fonts[members.Value.Item2];
						return true;
					}
					else
					{
						font = null;
						return false;
					}
				}

/// <summary>Returns the font.</summary>
				public static IFontMin GetFont(string name)
				{
					if (name == null)
						return null;

					FontMembers? members = Instance.GetFontFunc(name);

					if (members != null)
						return Instance.fonts[members.Value.Item2];
					else
						return null;
				}

/// <summary>Returns the font.</summary>
				public static IFontMin GetFont(int index) =>
					Instance.fonts[index];

/// <summary>Returns the styleindex.</summary>
				public static Vector2I GetStyleIndex(string name, FontStyles style = FontStyles.Regular)
				{
/// <summary>Returns the font.</summary>
					IFontMin font = GetFont(name);
					return new Vector2I(font?.Index ?? 0, (int)style);
				}
			}
		}
	}
}