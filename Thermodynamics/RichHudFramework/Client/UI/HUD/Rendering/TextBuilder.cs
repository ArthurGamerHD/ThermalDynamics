using System;
using System.Collections.Generic;
using System.Text;
using VRage;
using VRageMath;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework
{
	using RangeData = MyTuple<Vector2I, Vector2I>;
	using RangeFormatData = MyTuple<Vector2I, Vector2I, GlyphFormatMembers>;
	using RichStringMembers = MyTuple<StringBuilder, GlyphFormatMembers>;

	namespace UI
	{
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
			public abstract class TextBuilder : ITextBuilder
			{
				public IRichChar this[Vector2I index] => lines[index.X][index.Y];

				public ILine this[int index] => lines[index];

/// <summary>Returns the linecountfunc.</summary>
				public int Count => GetLineCountFunc();

				public GlyphFormat Format
				{
/// <summary>GlyphFormat operation.</summary>
					get { return new GlyphFormat((GlyphFormatMembers)GetOrSetMemberFunc(null, (int)TextBuilderAccessors.Format)); }
/// <summary>Returns the orsetmemberfunc.</summary>
					set { GetOrSetMemberFunc(value.Data, (int)TextBuilderAccessors.Format); }
				}

				public float LineWrapWidth
				{
/// <summary>return operation.</summary>
					get { return (float)GetOrSetMemberFunc(null, (int)TextBuilderAccessors.LineWrapWidth); }
/// <summary>Returns the orsetmemberfunc.</summary>
					set { GetOrSetMemberFunc(value, (int)TextBuilderAccessors.LineWrapWidth); }
				}

				public TextBuilderModes BuilderMode
				{
/// <summary>return operation.</summary>
					get { return (TextBuilderModes)GetOrSetMemberFunc(null, (int)TextBuilderAccessors.BuilderMode); }
/// <summary>Returns the orsetmemberfunc.</summary>
					set { GetOrSetMemberFunc(value, (int)TextBuilderAccessors.BuilderMode); }
				}

				protected readonly Func<object, int, object> GetOrSetMemberFunc;
				private readonly Func<int, int, object> GetLineMemberFunc;
				private readonly Func<int> GetLineCountFunc;
				private readonly Func<Vector2I, int, object> GetCharMemberFunc;
				private readonly Action<IList<RichStringMembers>, Vector2I> InsertTextAction;
				private readonly Action<IList<RichStringMembers>> SetTextAction;
				private readonly Action ClearAction;

				private readonly ReadOnlyApiCollection<ILine> lines;
				private RichText lastText;

/// <summary>TextBuilder operation.</summary>
				public TextBuilder(TextBuilderMembers data)
				{
					GetLineMemberFunc = data.Item1.Item1;
					GetLineCountFunc = data.Item1.Item2;

					GetCharMemberFunc = data.Item2;
					GetOrSetMemberFunc = data.Item3;
					InsertTextAction = data.Item4;
					SetTextAction = data.Item5;
					ClearAction = data.Item6;

/// <summary>ReadOnlyApiCollection operation.</summary>
					lines = new ReadOnlyApiCollection<ILine>(x => new LineData(this, x), GetLineCountFunc);
				}

/// <summary>Sets the text.</summary>
				public void SetText(RichText text)
				{
					SetTextAction(text.apiData);
					lastText = text;
				}

/// <summary>Sets the text.</summary>
				public void SetText(StringBuilder text, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(text, format ?? Format);
					SetTextAction(lastText.apiData);
				}

/// <summary>Sets the text.</summary>
				public void SetText(string text, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(text, format ?? Format);
					SetTextAction(lastText.apiData);
				}

/// <summary>Append operation.</summary>
				public void Append(RichText text)
				{
					InsertTextAction(text.apiData, GetLastIndex());
				}

/// <summary>Append operation.</summary>
				public void Append(StringBuilder text, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(text, format ?? Format);
					InsertTextAction(lastText.apiData, GetLastIndex());
				}

/// <summary>Append operation.</summary>
				public void Append(string text, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(text, format ?? Format);
					InsertTextAction(lastText.apiData, GetLastIndex());
				}

/// <summary>Append operation.</summary>
				public void Append(char ch, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(ch, format ?? Format);
					InsertTextAction(lastText.apiData, GetLastIndex());
				}

/// <summary>Insert operation.</summary>
				public void Insert(RichText text, Vector2I start)
				{
					InsertTextAction(text.apiData, start);
				}

/// <summary>Insert operation.</summary>
				public void Insert(StringBuilder text, Vector2I start, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(text, format ?? Format);
					InsertTextAction(lastText.apiData, start);
				}

/// <summary>Insert operation.</summary>
				public void Insert(string text, Vector2I start, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(text, format ?? Format);
					InsertTextAction(lastText.apiData, start);
				}

/// <summary>Insert operation.</summary>
				public void Insert(char ch, Vector2I start, GlyphFormat? format = null)
				{
					if (lastText == null)
/// <summary>RichText operation.</summary>
						lastText = new RichText();

					lastText.Clear();
					lastText.Add(ch, format ?? Format);
					InsertTextAction(lastText.apiData, start);
				}

/// <summary>Returns the text.</summary>
				public RichText GetText() =>
					GetTextRange(Vector2I.Zero, GetLastIndex() - new Vector2I(0, 1));

/// <summary>Returns the textrange.</summary>
				public RichText GetTextRange(Vector2I start, Vector2I end)
				{
/// <summary>Returns the orsetmemberfunc.</summary>
					var textData = GetOrSetMemberFunc(new RangeData(start, end), (int)TextBuilderAccessors.GetRange) as List<RichStringMembers>;

					if (lastText == null || lastText.apiData != textData)
/// <summary>RichText operation.</summary>
						lastText = new RichText(textData);

					return lastText;
				}

/// <summary>Sets the formatting.</summary>
				public void SetFormatting(GlyphFormat format)
				{
					GetOrSetMemberFunc(format.Data, (int)TextBuilderAccessors.Format);
					GetOrSetMemberFunc(new RangeFormatData(Vector2I.Zero, GetLastIndex() - new Vector2I(0, 1), format.Data), (int)TextBuilderAccessors.SetFormatting);
				}

/// <summary>Sets the formatting.</summary>
				public void SetFormatting(Vector2I start, Vector2I end, GlyphFormat format) =>
					GetOrSetMemberFunc(new RangeFormatData(start, end, format.Data), (int)TextBuilderAccessors.SetFormatting);

/// <summary>Removes the at.</summary>
				public void RemoveAt(Vector2I index) =>
					GetOrSetMemberFunc(new RangeData(index, index), (int)TextBuilderAccessors.RemoveRange);

/// <summary>Removes the range.</summary>
				public void RemoveRange(Vector2I start, Vector2I end) =>
					GetOrSetMemberFunc(new RangeData(start, end), (int)TextBuilderAccessors.RemoveRange);

/// <summary>Clear operation.</summary>
				public void Clear() =>
					ClearAction();

/// <summary>ToString operation.</summary>
				public override string ToString() =>
					GetOrSetMemberFunc(null, (int)TextBuilderAccessors.ToString) as string;

/// <summary>Returns the lastindex.</summary>
				protected Vector2I GetLastIndex()
				{
/// <summary>Returns the linecountfunc.</summary>
					int lineCount = GetLineCountFunc();
/// <summary>Vector2I operation.</summary>
					Vector2I start = new Vector2I(Math.Max(0, lineCount - 1), 0);

					if (lineCount > 0)
						start.Y = Math.Max(0, lines[start.X].Count);

					return start;
				}

				protected class LineData : ILine
				{
					public IRichChar this[int ch] => characters[ch];
					public int Count => (int)parent.GetLineMemberFunc(index, (int)LineAccessors.Count);
					public Vector2 Size => (Vector2)parent.GetLineMemberFunc(index, (int)LineAccessors.Size);
					public float VerticalOffset => (float)parent.GetLineMemberFunc(index, (int)LineAccessors.VerticalOffset);

					private readonly TextBuilder parent;
					private readonly int index;
					private readonly ReadOnlyApiCollection<IRichChar> characters;

/// <summary>LineData operation.</summary>
					public LineData(TextBuilder parent, int index)
					{
						this.parent = parent;
						this.index = index;

						characters = new ReadOnlyApiCollection<IRichChar>
						(
/// <summary>RichCharData operation.</summary>
							x => new RichCharData(parent, new Vector2I(index, x)),
							() => (int)parent.GetLineMemberFunc(index, (int)LineAccessors.Count)
						);
					}
				}

				protected class RichCharData : IRichChar
				{
					public char Ch => (char)parent.GetCharMemberFunc(index, (int)RichCharAccessors.Ch);
/// <summary>GlyphFormat operation.</summary>
					public GlyphFormat Format => new GlyphFormat((GlyphFormatMembers)parent.GetCharMemberFunc(index, (int)RichCharAccessors.Format));
					public Vector2 Size => (Vector2)parent.GetCharMemberFunc(index, (int)RichCharAccessors.Size);
					public Vector2 Offset => (Vector2)parent.GetCharMemberFunc(index, (int)RichCharAccessors.Offset);

					private readonly TextBuilder parent;
					private readonly Vector2I index;

/// <summary>RichCharData operation.</summary>
					public RichCharData(TextBuilder parent, Vector2I index)
					{
						this.parent = parent;
						this.index = index;
					}
				}
			}
		}
	}
}