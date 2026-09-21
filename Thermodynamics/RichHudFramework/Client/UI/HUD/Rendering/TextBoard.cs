using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework
{
	using FloatProp = MyTuple<Func<float>, Action<float>>;
	using RichStringMembers = MyTuple<StringBuilder, GlyphFormatMembers>;
	using Vec2Prop = MyTuple<Func<Vector2>, Action<Vector2>>;

	namespace UI
	{
		using UI.Client;
		using TextBuilderMembers = MyTuple<
			MyTuple<Func<int, int, object>, Func<int>>, // GetLineMember, GetLineCount
			Func<Vector2I, int, object>, // GetCharMember
			Func<object, int, object>, // GetOrSetMember
			Action<IList<RichStringMembers>, Vector2I>, // Insert
			Action<IList<RichStringMembers>>, // SetText
			Action // Clear
		>;

		namespace Rendering.Client
		{
			using TextBoardMembers = MyTuple<
				TextBuilderMembers,
				FloatProp, // Scale
				Func<Vector2>, // Size
				Func<Vector2>, // TextSize
				Vec2Prop, // FixedSize
				Action<BoundingBox2, BoundingBox2, MatrixD[]> // Draw 
			>;

            public sealed class TextBoard : TextBuilder, ITextBoard
			{
				public event Action TextChanged
				{
					add
					{
						var args = new MyTuple<bool, Action>(true, value);
						GetOrSetMemberFunc(args, (int)TextBoardAccessors.OnTextChanged);
					}
					remove
					{
						var args = new MyTuple<bool, Action>(false, value);
						GetOrSetMemberFunc(args, (int)TextBoardAccessors.OnTextChanged);
					}
				}

/// <summary>Returns the scalefunc.</summary>
				public float Scale { get { return GetScaleFunc(); } set { SetScaleAction(value); } }

/// <summary>Returns the sizefunc.</summary>
				public Vector2 Size => GetSizeFunc();

/// <summary>Returns the textsizefunc.</summary>
				public Vector2 TextSize => GetTextSizeFunc();

				public Vector2 TextOffset
				{
/// <summary>return operation.</summary>
					get { return (Vector2)GetOrSetMemberFunc(null, (int)TextBoardAccessors.TextOffset); }
/// <summary>Returns the orsetmemberfunc.</summary>
					set { GetOrSetMemberFunc(value, (int)TextBoardAccessors.TextOffset); }
				}

				public Vector2I VisibleLineRange => (Vector2I)GetOrSetMemberFunc(null, (int)TextBoardAccessors.VisibleLineRange);

/// <summary>Returns the fixedsizefunc.</summary>
				public Vector2 FixedSize { get { return GetFixedSizeFunc(); } set { SetFixedSizeAction(value); } }

				public bool AutoResize
				{
/// <summary>return operation.</summary>
					get { return (bool)GetOrSetMemberFunc(null, (int)TextBoardAccessors.AutoResize); }
/// <summary>Returns the orsetmemberfunc.</summary>
					set { GetOrSetMemberFunc(value, (int)TextBoardAccessors.AutoResize); }
				}

				public bool VertCenterText
				{
/// <summary>return operation.</summary>
					get { return (bool)GetOrSetMemberFunc(null, (int)TextBoardAccessors.VertAlign); }
/// <summary>Returns the orsetmemberfunc.</summary>
					set { GetOrSetMemberFunc(value, (int)TextBoardAccessors.VertAlign); }
				}

				private readonly Func<float> GetScaleFunc;
				private readonly Action<float> SetScaleAction;
				private readonly Func<Vector2> GetSizeFunc;
				private readonly Func<Vector2> GetTextSizeFunc;
				private readonly Func<Vector2> GetFixedSizeFunc;
				private readonly Action<Vector2> SetFixedSizeAction;
				private readonly Action<BoundingBox2, BoundingBox2, MatrixD[]> DrawAction;

/// <summary>TextBoard operation.</summary>
				public TextBoard() : this(HudMain.GetTextBoardData())
				{ }

/// <summary>TextBoard operation.</summary>
				private TextBoard(TextBoardMembers members) : base(members.Item1)
				{
					Format = GlyphFormat.Black;
					GetScaleFunc = members.Item2.Item1;
					SetScaleAction = members.Item2.Item2;
					GetSizeFunc = members.Item3;
					GetTextSizeFunc = members.Item4;
					GetFixedSizeFunc = members.Item5.Item1;
					SetFixedSizeAction = members.Item5.Item2;
					DrawAction = members.Item6;
				}

/// <summary>Draw operation.</summary>
				public void Draw(BoundingBox2 box, BoundingBox2 mask, MatrixD[] matrix) =>
					DrawAction(box, mask, matrix);

/// <summary>MoveToChar operation.</summary>
				public void MoveToChar(Vector2I index) =>
					GetOrSetMemberFunc(index, (int)TextBoardAccessors.MoveToChar);

/// <summary>Returns the charatoffset.</summary>
				public Vector2I GetCharAtOffset(Vector2 offset) =>
					(Vector2I)GetOrSetMemberFunc(offset, (int)TextBoardAccessors.GetCharAtOffset);
			}
		}

		namespace Rendering.Server
		{ }
	}
}