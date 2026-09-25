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
			MyTuple<Func<int, int, object>, Func<int>>,
			Func<Vector2I, int, object>,
			Func<object, int, object>,
			Action<IList<RichStringMembers>, Vector2I>,
			Action<IList<RichStringMembers>>,
			Action
		>;

		public class TextPage : TerminalPageBase, ITextPage
		{
			public RichText HeaderText
			{

				get { return new RichText(GetOrSetMemberFunc(null, (int)TextPageAccessors.GetOrSetHeader) as List<RichStringMembers>); }

				set { GetOrSetMemberFunc(value.apiData, (int)TextPageAccessors.GetOrSetHeader); }
			}

			public RichText SubHeaderText
			{

				get { return new RichText(GetOrSetMemberFunc(null, (int)TextPageAccessors.GetOrSetSubheader) as List<RichStringMembers>); }

				set { GetOrSetMemberFunc(value.apiData, (int)TextPageAccessors.GetOrSetSubheader); }
			}

			public RichText Text
			{

				get { return new RichText(GetOrSetMemberFunc(null, (int)TextPageAccessors.GetOrSetText) as List<RichStringMembers>); }

				set { GetOrSetMemberFunc(value.apiData, (int)TextPageAccessors.GetOrSetText); }
			}

			public ITextBuilder TextBuilder { get; }


			public TextPage() : base(ModPages.TextPage)
			{

				TextBuilder = new BasicTextBuilder((TextBuilderMembers)GetOrSetMemberFunc(null, (int)TextPageAccessors.GetTextBuilder));
			}

			private class BasicTextBuilder : TextBuilder
			{

				public BasicTextBuilder(TextBuilderMembers members) : base(members)
				{ }
			}
		}
	}
}