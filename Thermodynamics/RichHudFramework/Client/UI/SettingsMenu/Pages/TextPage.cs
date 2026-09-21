using RichHudFramework.UI.Rendering;
using RichHudFramework.UI.Rendering.Client;
using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework
{
	using RichStringMembers = MyTuple<StringBuilder, GlyphFormatMembers>;

	namespace UI.Client
	{
		using TextBuilderMembers = MyTuple<
			MyTuple<Func<int, int, object>, Func<int>>, // GetLineMember, GetLineCount
			Func<Vector2I, int, object>, // GetCharMember
			Func<object, int, object>, // GetOrSetMember
			Action<IList<RichStringMembers>, Vector2I>, // Insert
			Action<IList<RichStringMembers>>, // SetText
			Action // Clear
		>;

		public class TextPage : TerminalPageBase, ITextPage
		{
			public RichText HeaderText
			{
/// <summary>RichText operation.</summary>
				get { return new RichText(GetOrSetMemberFunc(null, (int)TextPageAccessors.GetOrSetHeader) as List<RichStringMembers>); }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value.apiData, (int)TextPageAccessors.GetOrSetHeader); }
			}

			public RichText SubHeaderText
			{
/// <summary>RichText operation.</summary>
				get { return new RichText(GetOrSetMemberFunc(null, (int)TextPageAccessors.GetOrSetSubheader) as List<RichStringMembers>); }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value.apiData, (int)TextPageAccessors.GetOrSetSubheader); }
			}

			public RichText Text
			{
/// <summary>RichText operation.</summary>
				get { return new RichText(GetOrSetMemberFunc(null, (int)TextPageAccessors.GetOrSetText) as List<RichStringMembers>); }
/// <summary>Returns the orsetmemberfunc.</summary>
				set { GetOrSetMemberFunc(value.apiData, (int)TextPageAccessors.GetOrSetText); }
			}

			public ITextBuilder TextBuilder { get; }

/// <summary>TextPage operation.</summary>
			public TextPage() : base(ModPages.TextPage)
			{
/// <summary>BasicTextBuilder operation.</summary>
				TextBuilder = new BasicTextBuilder((TextBuilderMembers)GetOrSetMemberFunc(null, (int)TextPageAccessors.GetTextBuilder));
			}

			private class BasicTextBuilder : TextBuilder
			{
/// <summary>BasicTextBuilder operation.</summary>
				public BasicTextBuilder(TextBuilderMembers members) : base(members)
				{ }
			}
		}
	}
}