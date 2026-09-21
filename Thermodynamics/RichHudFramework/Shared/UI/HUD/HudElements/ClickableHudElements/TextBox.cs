using RichHudFramework.UI.Rendering;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using VRageMath;

namespace RichHudFramework.UI
{
	using Client;
    using Server;

	public class TextBox : Label, IClickableElement, IBindInputElement, IValueControl<ITextBuilder>
	{
		public event EventHandler ValueChanged;

		public EventHandler UpdateValueCallback { set { ValueChanged += value; } }

		public ITextBuilder Value => TextBoard;

        public bool EnableEditing { get { return caret.ShowCaret; } set { caret.ShowCaret = value; } }

		public bool EnableHighlighting { get; set; }

		public bool InputOpen { get; private set; }

		public Func<char, bool> CharFilterFunc { get; set; }

        public Vector2I CaretPosition => Vector2I.Max(caret.CaretIndex, Vector2I.Zero);

		public Vector2I SelectionStart => selectionBox.Start;

		public Vector2I SelectionEnd => selectionBox.End;

		public bool SelectionEmpty => selectionBox.Empty;

		public bool MoveToEndOnGainFocus { get; set; }

		public bool ClearSelectionOnLoseFocus { get; set; }

		public char NewLineChar { get; set; }

		public IFocusHandler FocusHandler { get; }

		public IBindInput BindInput { get; }

		public IMouseInput MouseInput { get; }

		public override bool IsMousedOver => MouseInput.IsMousedOver;

		protected readonly MouseInputElement _mouseInput;
		protected readonly BindInputElement _bindInput;
		protected readonly ToolTip warningToolTip;

		private readonly TextInput textInput;
		private readonly TextCaret caret;
		private readonly SelectionBox selectionBox;
		private bool canHighlight, isHighlighting, allowInput, textUpdatePending;
		private Vector2I lastCaretIndex;

/// <summary>Vector2I operation.</summary>
		protected static readonly Vector2I caretMin = new Vector2I(0, -1);

/// <summary>TextBox operation.</summary>
		public TextBox(HudParentBase parent) : base(parent)
		{
/// <summary>InputFocusHandler operation.</summary>
			FocusHandler = new InputFocusHandler(this)
			{
				GainedInputFocusCallback = GainFocus,
				LostInputFocusCallback = LoseFocus
			};
/// <summary>MouseInputElement operation.</summary>
			_mouseInput = new MouseInputElement(this)
			{
				ShareCursor = true,
				ZOffset = 1,
				LeftClickedCallback = ClearSelection
			};
/// <summary>BindInputElement operation.</summary>
			_bindInput = new BindInputElement(this)
			{
				InputPredicate = GetCanAllowInput,
				InputFilter = SeBlacklistModes.Chat,
				CollectionInitializer = 
				{
                    { SharedBinds.Copy, CopyText },
					{ SharedBinds.Cut, CutText },
					{ SharedBinds.Paste, PasteText },
					{ SharedBinds.SelectAll, SelectAllText },
					{ SharedBinds.Escape, ClearSelection }
                }
			};

			MouseInput = _mouseInput;
			BindInput = _bindInput;
/// <summary>TextInput operation.</summary>
			textInput = new TextInput(AddChar, RemoveLastChar, TextInputFilter);
/// <summary>TextCaret operation.</summary>
			caret = new TextCaret(this) { Visible = false };
/// <summary>SelectionBox operation.</summary>
			selectionBox = new SelectionBox(caret, this) { Color = new Color(255, 255, 255, 140) };

/// <summary>ToolTip operation.</summary>
			warningToolTip = new ToolTip()
			{
				text = "Open Chat to Enable Text Editing",
				bgColor = ToolTip.OrangeWarningBG
			};

			TextBoard.TextChanged += HandleTextChange;

			EnableEditing = true;
			EnableHighlighting = true;
			NewLineChar = '\n';

			MoveToEndOnGainFocus = false;
			ClearSelectionOnLoseFocus = true;
/// <summary>Vector2 operation.</summary>
			Size = new Vector2(60f, 200f);
		}

/// <summary>TextBox operation.</summary>
		public TextBox() : this(null)
		{ }

/// <summary>OpenInput operation.</summary>
		public void OpenInput()
		{
			allowInput = true;
			UpdateInputOpen();
			caret.SetPosition(0);
			caret.SetPosition(int.MaxValue);
			textUpdatePending = false;
		}

/// <summary>CloseInput operation.</summary>
		public void CloseInput()
		{
			allowInput = false;
			UpdateInputOpen();
			selectionBox.ClearSelection();
		}

/// <summary>Sets the selection.</summary>
		public void SetSelection(Vector2I start, Vector2I end) =>
			selectionBox.SetSelection(start, end);

/// <summary>ClearSelection operation.</summary>
		public void ClearSelection()
		{
            selectionBox.ClearSelection();
            isHighlighting = false;
		}

/// <summary>HandleTextChange operation.</summary>
		protected virtual void HandleTextChange()
		{
			if (ValueChanged != null)
				textUpdatePending = true;
		}

/// <summary>TextInputFilter operation.</summary>
		private bool TextInputFilter(char ch)
		{
			if (CharFilterFunc == null)
				return ch >= ' ' || ch == '\n' || ch == '\t';
			else
				return CharFilterFunc(ch) && (ch >= ' ' || ch == '\n');
		}

/// <summary>GainFocus operation.</summary>
		protected virtual void GainFocus(object sender, EventArgs args)
		{
			if (MoveToEndOnGainFocus)
				caret.SetPosition(int.MaxValue);
		}

/// <summary>LoseFocus operation.</summary>
		protected virtual void LoseFocus(object sender, EventArgs args)
		{
			if (ClearSelectionOnLoseFocus)
				ClearSelection();
		}

/// <summary>CopyText operation.</summary>
		protected virtual void CopyText(object sender, EventArgs args)
		{
			if (EnableHighlighting && !selectionBox.Empty)
			{
				HudMain.ClipBoard = TextBoard.GetTextRange(selectionBox.Start, selectionBox.End);
			}
		}

/// <summary>CutText operation.</summary>
		protected virtual void CutText(object sender, EventArgs args)
		{
			if (EnableEditing && !selectionBox.Empty && EnableHighlighting)
			{
				RichText text = TextBoard.GetTextRange(selectionBox.Start, selectionBox.End);
				DeleteSelection();
				HudMain.ClipBoard = text;
			}
		}

/// <summary>PasteText operation.</summary>
		protected virtual void PasteText(object sender, EventArgs args)
		{
			if (EnableEditing)
			{
				if (HudMain.ClipBoard != null)
				{
/// <summary>Vector2I operation.</summary>
					Vector2I insertIndex = caret.CaretIndex + new Vector2I(0, 1);
					insertIndex.X = MathHelper.Clamp(insertIndex.X, 0, TextBoard.Count);

					DeleteSelection();
					TextBoard.Insert(HudMain.ClipBoard, insertIndex);
/// <summary>Returns the richtextminlength.</summary>
					int length = GetRichTextMinLength(HudMain.ClipBoard);

					if (caret.CaretIndex.Y == -1)
						length++;

					caret.Move(new Vector2I(0, length));
				}
			}
		}

/// <summary>SelectAllText operation.</summary>
		protected virtual void SelectAllText(object sender, EventArgs args)
		{
			if (EnableHighlighting)
			{
				caret.SetPosition(int.MaxValue);
				lastCaretIndex = caret.CaretIndex;
				selectionBox.SetSelection(new Vector2I(0, -1), new Vector2I(TextBoard.Count - 1, TextBoard[TextBoard.Count - 1].Count - 1));
				isHighlighting = true;
			}
		}

/// <summary>ClearSelection operation.</summary>
		protected virtual void ClearSelection(object sender, EventArgs args)
		{
			if (EnableHighlighting)
				ClearSelection();
		}

/// <summary>Adds a char.</summary>
		private void AddChar(char ch)
		{
			ch = (ch == NewLineChar) ? '\n' : ch;

			if (isHighlighting)
				DeleteSelection();
			else
				ClearSelection();

			TextBoard.Insert(ch, caret.CaretIndex + new Vector2I(0, 1));
			caret.Move(new Vector2I(0, 1));
		}

/// <summary>Removes the lastchar.</summary>
		private void RemoveLastChar()
		{
			if (TextBoard.Count > 0 && TextBoard[caret.CaretIndex.X].Count > 0 && caret.CaretIndex != caretMin)
			{
                if (isHighlighting)
					DeleteSelection();
				else
				{
					ClearSelection();

                    if (caret.CaretIndex.Y >= 0)
                        TextBoard.RemoveAt(ClampIndex(caret.CaretIndex, TextBoard));

					caret.Move(new Vector2I(0, -1));
				}
			}
		}

/// <summary>DeleteSelection operation.</summary>
		private void DeleteSelection()
		{
			if (!selectionBox.Empty)
				TextBoard.RemoveRange(selectionBox.Start, selectionBox.End);

            caret.SetPosition(selectionBox.Start);
			caret.Move(new Vector2I(0, -1));
            ClearSelection();
        }

/// <summary>Returns the canallowinput.</summary>
		private bool GetCanAllowInput() =>
			(allowInput || (FocusHandler.HasFocus && HudMain.InputMode == HudInputMode.Full));

/// <summary>UpdateInputOpen operation.</summary>
		private void UpdateInputOpen() =>
/// <summary>Returns the canallowinput.</summary>
			InputOpen = GetCanAllowInput() && (EnableHighlighting || EnableEditing);

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
/// <summary>Returns the canallowinput.</summary>
			bool useInput = GetCanAllowInput();

			if (EnableEditing && MouseInput.IsMousedOver && HudMain.InputMode == HudInputMode.CursorOnly)
				HudMain.Cursor.RegisterToolTip(warningToolTip);

			if (useInput && EnableEditing)
                textInput.HandleInput();

			UpdateInputOpen();
			caret.Visible = InputOpen;

			if (useInput && EnableHighlighting)
			{
				if (caret.IsNavigating)
				{
					if ((!MouseInput.IsNewLeftClicked && MouseInput.IsLeftClicked) || SharedBinds.Shift.IsPressed)
						canHighlight = true;
					else
						canHighlight = false;

					if (canHighlight || isHighlighting)
						selectionBox.UpdateSelection();

					if (isHighlighting && selectionBox.Start.X == selectionBox.End.X)
						isHighlighting = selectionBox.End.Y >= selectionBox.Start.Y;

					if (!isHighlighting && lastCaretIndex != caret.CaretIndex)
						isHighlighting = canHighlight;
				}
			}
			else
				canHighlight = false;

			if (!canHighlight && lastCaretIndex != caret.CaretIndex)
				ClearSelection();

			lastCaretIndex = caret.CaretIndex;
			selectionBox.Visible = isHighlighting;

			if (!InputOpen && textUpdatePending)
			{
				ValueChanged?.Invoke(FocusHandler?.InputOwner, EventArgs.Empty);
				textUpdatePending = false;
			}
		}

/// <summary>ClampIndex operation.</summary>
		private static Vector2I ClampIndex(Vector2I index, ITextBuilder text)
		{
			if (text.Count > 0)
			{
				index.X = MathHelper.Clamp(index.X, 0, text.Count - 1);
				index.Y = MathHelper.Clamp(index.Y, 0, text[index.X].Count - 1);

				return index;
			}
			else
				return Vector2I.Zero;
		}

/// <summary>Returns the richtextminlength.</summary>
		private static int GetRichTextMinLength(RichText text)
		{
			int length = 0;

			for (int n = 0; n < text.apiData.Count; n++)
				length += text.apiData[n].Item1.Length;

			return length;
		}

		private class TextCaret : TexturedBox
		{
			public Vector2I CaretIndex { get; private set; }

			public bool ShowCaret { get; set; }

			public bool IsNavigating { get; private set; }

			private readonly TextBox textElement;
			private readonly ITextBoard text;
			private readonly Stopwatch blinkTimer;
			private bool blink;
			private int caretOffset;
			private Vector2 lastCursorPos;

/// <summary>TextCaret operation.</summary>
			public TextCaret(TextBox textElement) : base(textElement)
			{
				this.textElement = textElement;
				text = textElement.TextBoard;
/// <summary>Vector2 operation.</summary>
				Size = new Vector2(1f, 16f);
/// <summary>Color operation.</summary>
				Color = new Color(240, 240, 230);

/// <summary>Stopwatch operation.</summary>
				blinkTimer = new Stopwatch();
				blinkTimer.Start();
			}

/// <summary>Move operation.</summary>
			public void Move(Vector2I dir, bool navigate = false)
			{
				bool moveLeft = dir.Y < 0,
					moveRight = dir.Y > 0;

				if (CaretIndex == caretMin && moveLeft || (text.Count == 0 || text.Count == 1 && text[0].Count == 0))
					return;

				IRichChar ch = text[ClampIndex(CaretIndex, text)];
				Vector2I newIndex;

				bool isPrepending = CaretIndex.Y == -1,
					isPrependStarting = CaretIndex.Y == 0 && (moveLeft && (navigate || CaretIndex.X == 0)) && ch.Ch != '\n';

				if (isPrependStarting || (dir.Y == 0 && isPrepending))
				{
/// <summary>Vector2I operation.</summary>
					newIndex = CaretIndex + new Vector2I(dir.X, 0);
					newIndex.Y = -1;

/// <summary>ClampCaret operation.</summary>
					newIndex = ClampCaret(newIndex);
/// <summary>Returns the offsetfromindex.</summary>
					caretOffset = GetOffsetFromIndex(new Vector2I(newIndex.X, 0));
				}
				else
				{
					int newOffset = Math.Max(caretOffset + dir.Y, 0);

					if ((isPrepending && moveRight) && (CaretIndex.X > 0 || text[0].Count > 1))
						newOffset -= 1;

/// <summary>Returns the indexfromoffset.</summary>
					newIndex = GetIndexFromOffset(newOffset) + new Vector2I(dir.X, 0);
/// <summary>ClampCaret operation.</summary>
					newIndex = ClampCaret(newIndex);
/// <summary>Returns the offsetfromindex.</summary>
					caretOffset = GetOffsetFromIndex(newIndex);

					ch = text[ClampIndex(newIndex, text)];

					if (navigate && moveRight && newIndex.X > CaretIndex.X && ch.Ch != '\n')
						newIndex.Y = -1;
				}

				CaretIndex = newIndex;

				if (CaretIndex.Y >= 0)
					text.MoveToChar(CaretIndex);
				else
					text.MoveToChar(CaretIndex + new Vector2I(0, 1));

				blink = true;
				blinkTimer.Restart();

				IsNavigating = navigate;
			}

/// <summary>Sets the position.</summary>
			public void SetPosition(Vector2I index)
			{
/// <summary>ClampCaret operation.</summary>
				CaretIndex = ClampCaret(index);
				caretOffset = Math.Max(GetOffsetFromIndex(CaretIndex), 0);
				text.MoveToChar(CaretIndex);
			}

/// <summary>Sets the position.</summary>
			public void SetPosition(int offset) =>
                SetPosition(GetIndexFromOffset(offset));

/// <summary>Draw operation.</summary>
            protected override void Draw()
			{
				if (ShowCaret)
				{
					bool isCharVisible = text.Count == 0 || text[0].Count == 0;
/// <summary>ClampCaret operation.</summary>
					CaretIndex = ClampCaret(CaretIndex);

					if ((text.Count > 0 && text[0].Count > 0) &&
						(CaretIndex.X >= text.VisibleLineRange.X && CaretIndex.X <= text.VisibleLineRange.Y))
					{
						Vector2I index = Vector2I.Max(CaretIndex, Vector2I.Zero);

						IRichChar ch = text[index];
						Vector2 size = ch.Size,
							pos = ch.Offset + text.TextOffset;
						BoundingBox2 textBounds = BoundingBox2.CreateFromHalfExtent(Vector2.Zero, .5f * text.Size),
							charBounds = BoundingBox2.CreateFromHalfExtent(pos, .5f * Vector2.Max(size, new Vector2(8f)));

						if (textBounds.Contains(charBounds) != ContainmentType.Disjoint)
							isCharVisible = true;
					}

					if (blink & isCharVisible)
					{
						UpdateOffset();
						base.Draw();
					}

					if (blinkTimer.ElapsedMilliseconds > 500)
					{
						blink = !blink;
						blinkTimer.Restart();
					}
				}
			}

/// <summary>UpdateOffset operation.</summary>
			private void UpdateOffset()
			{
/// <summary>Vector2 operation.</summary>
				Vector2 offset = new Vector2();
				Vector2I index = Vector2I.Max(CaretIndex, Vector2I.Zero);

				if (text.Count > 0 && text[index.X].Count > 0)
				{
					IRichChar ch;
					Height = text[index.X].Size.Y - 2f;
					ch = text[index];

					if (CaretIndex.Y == -1)
					{
						offset = ch.Offset + text.TextOffset;
						offset.X -= ch.Size.X * .5f + 1f;
					}
					else
					{
						offset = ch.Offset + text.TextOffset;
						offset.X += ch.Size.X * .5f + 1f;
					}
				}
				else
				{
					if (text.Format.Alignment == TextAlignment.Left)
						offset.X = -textElement.Size.X * .5f + 2f;
/// <summary>if operation.</summary>
					else if (text.Format.Alignment == TextAlignment.Right)
						offset.X = textElement.Size.X * .5f - 2f;

					var parentFull = Parent as HudElementBase;
					offset += parentFull.Padding * .5f;

					if (!text.VertCenterText)
						offset.Y = (text.Size.Y - Height) * .5f - 4f;
				}

				Offset = offset;
			}

/// <summary>HandleInput operation.</summary>
			protected override void HandleInput(Vector2 cursorPos)
			{
				if (SharedBinds.DownArrow.IsPressedAndHeld || SharedBinds.DownArrow.IsNewPressed)
					Move(new Vector2I(1, 0), true);

				if (SharedBinds.UpArrow.IsPressedAndHeld || SharedBinds.UpArrow.IsNewPressed)
					Move(new Vector2I(-1, 0), true);

				if (SharedBinds.RightArrow.IsPressedAndHeld || SharedBinds.RightArrow.IsNewPressed)
					Move(new Vector2I(0, 1), true);

				if (SharedBinds.LeftArrow.IsPressedAndHeld || SharedBinds.LeftArrow.IsNewPressed)
					Move(new Vector2I(0, -1), true);

				if (textElement.MouseInput.IsLeftClicked)
					GetClickedChar(cursorPos);
			}

/// <summary>Returns the clickedchar.</summary>
			private void GetClickedChar(Vector2 cursorPos)
			{
				if ((cursorPos - lastCursorPos).LengthSquared() > 4f)
				{
/// <summary>ClampCaret operation.</summary>
					CaretIndex = ClampCaret(CaretIndex);

					Vector2 offset = cursorPos - textElement.Position;
					Vector2I index = Vector2I.Max(CaretIndex, Vector2I.Zero),
						newIndex = text.GetCharAtOffset(offset);

					if (text.Count > newIndex.X && text[newIndex.X].Count > newIndex.Y)
					{
						IRichChar clickedCh = text[newIndex];

						if (offset.X <= clickedCh.Offset.X)
/// <summary>Vector2I operation.</summary>
							newIndex -= new Vector2I(0, 1);

/// <summary>ClampCaret operation.</summary>
						CaretIndex = ClampCaret(newIndex);
/// <summary>Returns the offsetfromindex.</summary>
						caretOffset = GetOffsetFromIndex(CaretIndex);
						lastCursorPos = cursorPos;

						blink = true;
						blinkTimer.Restart();
						IsNavigating = true;
					}
				}
			}

/// <summary>ClampCaret operation.</summary>
			private Vector2I ClampCaret(Vector2I index)
			{
				if (text.Count > 0)
				{
					index.X = MathHelper.Clamp(index.X, 0, text.Count - 1);
					index.Y = MathHelper.Clamp(index.Y, -1, text[index.X].Count - 1);

					if (index.Y == -1 && text[index.X].Count > 0 && text[index.X][0].Ch == '\n')
						index.Y = 0;

					return index;
				}
				else
					return Vector2I.Zero;
			}

/// <summary>Returns the offsetfromindex.</summary>
			private int GetOffsetFromIndex(Vector2I index)
			{
				int offset = 0;

				for (int line = 0; line < index.X; line++)
				{
					offset += text[line].Count;
				}

				offset += index.Y;
				return Math.Max(offset, 0);
			}

/// <summary>Returns the indexfromoffset.</summary>
			private Vector2I GetIndexFromOffset(int offset)
			{
				Vector2I index = Vector2I.Zero;
				int charCount = 0;

				for (int line = 0; line < text.Count; line++)
					charCount += text[line].Count;

				offset = Math.Min(offset, charCount - 1);

				for (int line = 0; line < text.Count; line++)
				{
					int lineLength = text[line].Count;

					if (offset < lineLength)
					{
						index.Y = offset;
						break;
					}
					else
					{
						offset -= lineLength;
						index.X++;
					}
				}

				return index;
			}
		}

		private class SelectionBox : HudElementBase
		{
			public Color Color { get { return highlightBoard.Color; } set { highlightBoard.Color = value; } }

			public Vector2I Start { get; private set; }

			public Vector2I End { get; private set; }

			public bool Empty => (Start == -Vector2I.One || End == -Vector2I.One);

			private readonly TextCaret caret;
			private readonly ITextBoard text;
			private readonly MatBoard highlightBoard;
			private readonly List<HighlightBox> highlightList;
			private Vector2I selectionAnchor;

/// <summary>SelectionBox operation.</summary>
			public SelectionBox(TextCaret caret, Label parent) : base(parent)
			{
				text = parent.TextBoard;
				this.caret = caret;

				Start = -Vector2I.One;
				selectionAnchor = -Vector2I.One;
/// <summary>MatBoard operation.</summary>
				highlightBoard = new MatBoard();
/// <summary>List operation.</summary>
				highlightList = new List<HighlightBox>();

				text.TextChanged += ClearSelection;
			}

/// <summary>Sets the selection.</summary>
			public void SetSelection(Vector2I start, Vector2I end)
			{
				Start = start;
				End = end;
				selectionAnchor = start;
			}

/// <summary>ClearSelection operation.</summary>
			public void ClearSelection()
			{
				Start = -Vector2I.One;
				End = -Vector2I.One;
				selectionAnchor = -Vector2I.One;
				highlightList.Clear();
			}

/// <summary>UpdateSelection operation.</summary>
			public void UpdateSelection()
			{
				if (text.Count > 0)
				{
					Vector2I caretIndex = caret.CaretIndex;
					bool wasSelecting = selectionAnchor != -Vector2I.One;

                    if (!wasSelecting)
                        selectionAnchor = caretIndex;

					bool isAfterAnchor;

					if (caretIndex.X < selectionAnchor.X)
						isAfterAnchor = false;
/// <summary>if operation.</summary>
					else if (caretIndex.X > selectionAnchor.X)
						isAfterAnchor = true;
					else // Same line
						isAfterAnchor = (caretIndex.Y >= selectionAnchor.Y);

					if (isAfterAnchor)
					{
                        Start = selectionAnchor;
						End = caretIndex;
                    }
					else
					{
                        Start = caretIndex;
						End = selectionAnchor;                       
                    }

                    if (Start.Y < text[Start.X].Count - 1)
/// <summary>Vector2I operation.</summary>
                        Start += new Vector2I(0, 1);

/// <summary>ClampIndex operation.</summary>
                    Start = ClampIndex(Start, text);
/// <summary>ClampIndex operation.</summary>
					End = ClampIndex(End, text);
				}

                else
				{
					Start = -Vector2I.One;
					End = -Vector2I.One;
					selectionAnchor = -Vector2I.One;
				}
			}

/// <summary>Draw operation.</summary>
			protected override void Draw()
			{
				if (!Empty)
				{
					UpdateHighlight();

					Vector2 highlightOffset = Origin + text.TextOffset;
/// <summary>BoundingBox2 operation.</summary>
					BoundingBox2 bounds = new BoundingBox2(-text.Size * .5f, text.Size * .5f);
					bounds.Translate(Origin + Offset);

					for (int n = 0; n < highlightList.Count; n++)
						highlightList[n].Draw(highlightBoard, highlightOffset, bounds, HudSpace.PlaneToWorldRef);
				}
			}

/// <summary>UpdateHighlight operation.</summary>
			private void UpdateHighlight()
			{
				highlightList.Clear();

				Vector2I lineRange = text.VisibleLineRange;
/// <summary>ClampIndex operation.</summary>
				Start = ClampIndex(Start, text);
/// <summary>ClampIndex operation.</summary>
				End = ClampIndex(End, text);

				int startLine = Math.Max(Start.X, lineRange.X),
					endLine = Math.Min(End.X, lineRange.Y);

				if (Start.X == End.X && Start.X == startLine)
					AddHighlightBox(Start.X, Start.Y, End.Y);
				else
				{
					for (int line = startLine; line <= endLine; line++)
					{
						if (line == Start.X)
							AddHighlightBox(Start.X, Start.Y, text[Start.X].Count - 1); // Top
/// <summary>if operation.</summary>
						else if (line == End.X)
							AddHighlightBox(End.X, 0, End.Y); // Bottom
						else
							AddHighlightBox(line, 0, text[line].Count - 1); // Middle
					}
				}

				if (highlightList.Count > 10 && highlightList.Capacity > 3 * highlightList.Count)
					highlightList.TrimExcess();
			}

/// <summary>Adds a highlightbox.</summary>
			private void AddHighlightBox(int lineIdx, int startCh, int endCh)
			{
				var line = text[lineIdx];
				if (line.Count == 0) return;

				startCh = Math.Max(0, Math.Min(startCh, line.Count - 1));
				endCh = Math.Min(endCh, line.Count - 1);

				IRichChar startChar = line[startCh],
					endChar = line[endCh],
					prevChar = startCh > 0 ? line[startCh - 1] : null;

				float startLeft = startChar.Offset.X - 0.5f * startChar.Size.X;

				if (prevChar != null)
				{
					float prevRight = prevChar.Offset.X + 0.5f * prevChar.Size.X;
					startLeft = Math.Max(startLeft, prevRight);
				}

				float endRight = endChar.Offset.X + 0.5f * endChar.Size.X;

				float width = Math.Max(endRight - startLeft, 1f);
				float centerX = startLeft + width * 0.5f;
				float centerY = line.VerticalOffset - line.Size.Y * 0.5f;

				var box = new HighlightBox
				{
/// <summary>Vector2 operation.</summary>
					size = new Vector2(width, line.Size.Y),
/// <summary>Vector2 operation.</summary>
					offset = new Vector2(centerX, centerY)
				};

				highlightList.Add(box);
			}

			private struct HighlightBox
			{
				public Vector2 size, offset;

/// <summary>Draw operation.</summary>
				public void Draw(MatBoard matBoard, Vector2 highlightOffset, BoundingBox2 tbBounds, MatrixD[] matrixRef)
				{
/// <summary>default operation.</summary>
					CroppedBox box = default(CroppedBox);
					Vector2 highlightPos = highlightOffset + offset,
						halfSize = 0.5f * size;

/// <summary>BoundingBox2 operation.</summary>
					box.bounds = new BoundingBox2(highlightPos - halfSize, highlightPos + halfSize);
					box.mask = tbBounds;

					matBoard.Draw(ref box, matrixRef);
				}
			}
		}
	}
}