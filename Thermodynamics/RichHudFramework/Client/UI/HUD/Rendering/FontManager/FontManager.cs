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
		string,
		int,
		float,
		float,
		Func<int, bool>,
		ApiMemberAccessor
	>;
	using FontStyleDefinition = MyTuple<
		int,
		float,
		float,
		AtlasMembers[],
		KeyValuePair<char, GlyphMembers>[],
		KeyValuePair<uint, float>[]
	>;

	namespace UI
	{
		using FontDefinition = MyTuple<
			string,
			float,
			FontStyleDefinition[]
		>;

		namespace Rendering.Client
		{
			using FontManagerMembers = MyTuple<
				MyTuple<Func<int, FontMembers>, Func<int>>,
				Func<FontDefinition, FontMembers?>,
				Func<string, FontMembers?>,
				ApiMemberAccessor
			>;

			public sealed partial class FontManager : RichHudClient.ApiModule
			{
				public static Vector2I Default => Vector2I.Zero;

				public static IReadOnlyList<IFontMin> Fonts => Instance.fonts;

				private static FontManager Instance
				{

					get { Init(); return instance; }
					set { instance = value; }
				}
				private static FontManager instance;

				private readonly ReadOnlyApiCollection<IFontMin> fonts;
				private readonly Func<FontDefinition, FontMembers?> TryAddFontFunc;
				private readonly Func<string, FontMembers?> GetFontFunc;


				private FontManager() : base(ApiModuleTypes.FontManager, false, true)
				{
					var members = (FontManagerMembers)GetApiData();


					Func<int, IFontMin> fontGetter = x => new FontData(members.Item1.Item1(x));

					fonts = new ReadOnlyApiCollection<IFontMin>(fontGetter, members.Item1.Item2);

					TryAddFontFunc = members.Item2;
					GetFontFunc = members.Item3;
				}


				private static void Init()
				{
					if (instance == null)

						instance = new FontManager();
				}


				public override void Close()
				{
					instance = null;
				}


				public static bool TryAddFont(FontDefinition fontData) =>
					Instance.TryAddFontFunc(fontData) != null;


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


				public static IFontMin GetFont(int index) =>
					Instance.fonts[index];


				public static Vector2I GetStyleIndex(string name, FontStyles style = FontStyles.Regular)
				{

					IFontMin font = GetFont(name);
					return new Vector2I(font?.Index ?? 0, (int)style);
				}
			}
		}
	}
}