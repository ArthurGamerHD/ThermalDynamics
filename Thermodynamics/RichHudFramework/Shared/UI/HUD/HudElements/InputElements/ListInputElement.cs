using System;
using VRageMath;

namespace RichHudFramework.UI
{
	public class ListInputElement<TElementContainer, TElement> : MouseInputElement
		where TElement : HudElementBase, IMinLabelElement

		where TElementContainer : class, IScrollBoxEntry<TElement>, new()
	{
		public event EventHandler SelectionChanged;

		public IReadOnlyHudCollection<TElementContainer, TElement> Entries { get; }

		public TElementContainer Selection
		{
			get
			{
				if (Entries.Count == 0 || SelectionIndex < 0 || SelectionIndex >= Entries.Count)
				{

					return default(TElementContainer);
				}
				else
				{
					return Entries[SelectionIndex];
				}
			}
		}

		public int SelectionIndex => MathHelper.Clamp(_selectionIndex, -1, Entries.Count - 1);

		public int HighlightIndex => MathHelper.Clamp(_highlightIndex, 0, Entries.Count - 1);

		public int FocusIndex => MathHelper.Clamp(_focusIndex, 0, Entries.Count - 1);

		public bool KeyboardScroll { get; protected set; }

		public Vector2I ListRange { get; set; }

		public Vector2 ListSize { get; set; }

		public Vector2 ListPos { get; set; }

		protected Vector2 lastCursorPos;
		private int _selectionIndex;
		private int _highlightIndex;
		private int _focusIndex;


		public ListInputElement(
			HudElementBase parent,
			IReadOnlyHudCollection<TElementContainer, TElement> entries

		) : base(parent)
		{
			Entries = entries;
			_selectionIndex = -1;
		}


		public ListInputElement(HudChain<TElementContainer, TElement> parent)
			: this(parent, parent)
		{ }


		public void SetSelectionAt(int index)
		{
			if (index != _selectionIndex)
			{
				_selectionIndex = MathHelper.Clamp(index, 0, Entries.Count - 1);
				Selection.Enabled = true;
				SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}


		public void SetSelection(TElementContainer member)
		{
			int index = Entries.FindIndex(x => member.Equals(x));

			if (index != -1 && index != _selectionIndex)
			{
				_selectionIndex = MathHelper.Clamp(index, 0, Entries.Count - 1);
				Selection.Enabled = true;
				SelectionChanged?.Invoke(this, EventArgs.Empty);
			}
		}


		public void OffsetSelectionIndex(int offset, bool wrap = false)
		{
			int index = _selectionIndex,
				dir = offset > 0 ? 1 : -1,
				absOffset = Math.Abs(offset);

			if (dir > 0)
			{
				for (int i = 0; i < absOffset; i++)
				{
					if (wrap)
						index = (index + dir) % Entries.Count;
					else
						index = Math.Min(index + dir, Entries.Count - 1);


					index = FindFirstEnabled(index, wrap);
				}
			}
			else
			{
				for (int i = 0; i < absOffset; i++)
				{
					if (wrap)
						index = (index + dir) % Entries.Count;
					else
						index = Math.Max(index + dir, 0);

					if (index < 0)
						index += Entries.Count;


					index = FindLastEnabled(index, wrap);
				}
			}

			SetSelectionAt(index);
		}


		public void ClearSelection()
		{
			_selectionIndex = -1;
			_highlightIndex = 0;
			_focusIndex = 0;
		}


		protected override void HandleInput(Vector2 cursorPos)
		{
			if (Entries.Count > 0)
			{
				base.HandleInput(cursorPos);
				UpdateSelectionInput(cursorPos);
			}
		}


		protected virtual void UpdateSelectionInput(Vector2 cursorPos)
		{
			_selectionIndex = MathHelper.Clamp(_selectionIndex, -1, Entries.Count - 1);
			_highlightIndex = MathHelper.Clamp(_highlightIndex, 0, Entries.Count - 1);

			if (KeyboardScroll)
				_focusIndex = _highlightIndex;
			else
				_focusIndex = _selectionIndex;

			if (FocusHandler?.HasFocus ?? false)
			{
				if (SharedBinds.UpArrow.IsNewPressed || SharedBinds.UpArrow.IsPressedAndHeld)
				{
					for (int i = _highlightIndex - 1; i >= 0; i--)
					{
						if (Entries[i].Enabled)
						{
							_highlightIndex = i;
							break;
						}
					}

					KeyboardScroll = true;
					lastCursorPos = cursorPos;
				}

				else if (SharedBinds.DownArrow.IsNewPressed || SharedBinds.DownArrow.IsPressedAndHeld)
				{
					for (int i = _highlightIndex + 1; i < Entries.Count; i++)
					{
						if (Entries[i].Enabled)
						{
							_highlightIndex = i;
							break;
						}
					}

					KeyboardScroll = true;
					lastCursorPos = cursorPos;
				}
			}
			else
			{
				KeyboardScroll = false;

				lastCursorPos = new Vector2(float.MinValue);
			}

			bool listMousedOver = false;

			if (IsMousedOver)
			{
				if ((cursorPos - lastCursorPos).LengthSquared() > 4f)
					KeyboardScroll = false;

				if (!KeyboardScroll)
				{
					Vector2 cursorOffset = cursorPos - ListPos;

					BoundingBox2 listBounds = new BoundingBox2(-ListSize * .5f, ListSize * .5f);

					if (listBounds.Contains(cursorOffset) == ContainmentType.Contains)
					{
						int newIndex = ListRange.X;

						for (int i = ListRange.X; i <= ListRange.Y; i++)
						{
							if (Entries[i].Enabled)
							{
								TElement element = Entries[i].Element;
								Vector2 halfSize = element.Size * .5f,
									offset = element.Offset;

								BoundingBox2 bb = new BoundingBox2(offset - halfSize, offset + halfSize);

								if (bb.Contains(cursorOffset) == ContainmentType.Contains)
									break;
							}

							newIndex++;
						}

						if (newIndex >= 0 && newIndex < Entries.Count)
						{
							_highlightIndex = newIndex;
							listMousedOver = true;
						}
					}
				}
			}

			if ((listMousedOver && SharedBinds.LeftButton.IsNewPressed) ||
				((FocusHandler?.HasFocus ?? false) && SharedBinds.Space.IsNewPressed))
			{
				_selectionIndex = _highlightIndex;
                var owner = (object)(FocusHandler?.InputOwner) ?? Parent;
                SelectionChanged?.Invoke(owner, EventArgs.Empty);
				KeyboardScroll = false;
			}

			_highlightIndex = MathHelper.Clamp(_highlightIndex, 0, Entries.Count - 1);
		}


		private int FindFirstEnabled(int index, bool wrap)
		{
			if (wrap)
			{
				int j = index;

				for (int n = 0; n < 2 * Entries.Count; n++)
				{
					if (Entries[j].Enabled)
						return j;

					j++;
					j %= Entries.Count;
				}
			}
			else
			{
				for (int n = index; n < Entries.Count; n++)
				{
					if (Entries[n].Enabled)
						return n;
				}
			}

			return -1;
		}


		private int FindLastEnabled(int index, bool wrap)
		{
			if (wrap)
			{
				int j = index;

				for (int n = 0; n < 2 * Entries.Count; n++)
				{
					if (Entries[j].Enabled)
						return j;

					j++;
					j %= Entries.Count;
				}
			}
			else
			{
				for (int n = index; n >= 0; n--)
				{
					if (Entries[n].Enabled)
						return n;
				}
			}

			return -1;
		}
	}
}
