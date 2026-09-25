using RichHudFramework.UI.Client;
using RichHudFramework.UI.Server;
using RichHudFramework.UI.Rendering;
using System;
using System.Collections.Generic;
using VRageMath;

namespace RichHudFramework.UI
{
	using static NodeConfigIndices;












	public class RadialSelectionBox<TContainer, TElement> : HudCollection<TContainer, TElement>
		where TContainer : IScrollBoxEntry<TElement>, new()
		where TElement : HudElementBase
	{



		public virtual IReadOnlyList<TContainer> EntryList => hudCollectionList;




		public virtual TContainer Selection
		{
			get
			{
				if (SelectionIndex >= 0 && SelectionIndex < hudCollectionList.Count)
					return hudCollectionList[SelectionIndex];
				return default(TContainer);
			}
		}




		public virtual TContainer HighlightedEntry
		{
			get
			{
				if (HighlightIndex >= 0 && HighlightIndex < hudCollectionList.Count)
					return hudCollectionList[HighlightIndex];
				return default(TContainer);
			}
		}




		public virtual int SelectionIndex { get; protected set; } = -1;




		public virtual int HighlightIndex { get; protected set; } = -1;





		public virtual int MaxEntryCount { get; set; } = 8;




		public virtual int EnabledCount { get; protected set; }




		public virtual bool UseGestureInput { get; set; } = false;




		public virtual Color BackgroundColor { get; set; }




		public virtual Color HighlightColor { get; set; }




		public virtual Color SelectionColor { get; set; }





		public float CursorSensitivity { get; set; }





        public float InnerRadius
		{
			get { return polyBoard.InnerRadius; }
            set { polyBoard.InnerRadius = value; }
        }






		protected readonly PuncturedPolyBoard polyBoard;







		protected int selectionVisPos;





		protected int highlightVisPos;





		protected int effectiveMaxCount;





		protected int minPolySize = 64;





		protected bool isStartPosStale = true;


		protected Vector2 lastCursorPos;





		protected Vector2 cursorNormal;

		public RadialSelectionBox(HudParentBase parent = null) : base(parent)
		{
			polyBoard = new PuncturedPolyBoard()
			{
				Sides = 64
			};


			BackgroundColor = new Color(70, 78, 86);
			HighlightColor = TerminalFormatting.DarkSlateGrey;
			SelectionColor = TerminalFormatting.Mint;

			Size = new Vector2(512f);
			MaxEntryCount = 8;
			CursorSensitivity = 0.5f;
			UseGestureInput = false;
			UseCursor = true;
			isStartPosStale = true;
		}




		public void SetSelectionAt(int index)
		{
			SelectionIndex = MathHelper.Clamp(index, 0, hudCollectionList.Count - 1);
			lastCursorPos = new Vector2(HudSpace.CursorPos.X, HudSpace.CursorPos.Y);
		}




		public void SetSelection(TContainer container)
		{
			int index = FindIndex(x => x.Equals(container));
			if (index != -1)
				SelectionIndex = index;

			lastCursorPos = new Vector2(HudSpace.CursorPos.X, HudSpace.CursorPos.Y);
		}




		public void SetHighlightAt(int index)
		{
			HighlightIndex = MathHelper.Clamp(index, 0, hudCollectionList.Count - 1);
			lastCursorPos = new Vector2(HudSpace.CursorPos.X, HudSpace.CursorPos.Y);
		}




		public void SetHighlight(TContainer container)
		{
			int index = FindIndex(x => x.Equals(container));
			if (index != -1)
				HighlightIndex = index;

			lastCursorPos = new Vector2(HudSpace.CursorPos.X, HudSpace.CursorPos.Y);
		}




		public override void Clear()
		{
			HighlightIndex = -1;
			SelectionIndex = -1;
			base.Clear();
		}




		public void ClearHighlight() => HighlightIndex = -1;




		public void ClearSelection() => SelectionIndex = -1;





		protected override void Layout()
		{

			EnabledCount = 0;
			SelectionIndex = MathHelper.Clamp(SelectionIndex, -1, hudCollectionList.Count - 1);
			HighlightIndex = MathHelper.Clamp(HighlightIndex, -1, hudCollectionList.Count - 1);
			CursorSensitivity = MathHelper.Clamp(CursorSensitivity, 0.3f, 2f);

			for (int i = 0; i < hudCollectionList.Count; i++)
			{
				if (hudCollectionList[i].Enabled)
				{
					hudCollectionList[i].Element.Visible = true;
					EnabledCount++;
				}
				else
				{
					hudCollectionList[i].Element.Visible = false;
				}
			}

			effectiveMaxCount = Math.Max(MaxEntryCount, EnabledCount);


			int sliceSize = polyBoard.Sides / effectiveMaxCount;
			Vector2I slice = new Vector2I(0, sliceSize - 1);
			Vector2 size = UnpaddedSize;

			for (int i = 0; i < hudCollectionList.Count; i++)
			{
				TContainer container = hudCollectionList[i];
				if (container.Enabled)
				{
					container.Element.Offset = 1.05f * polyBoard.GetSliceOffset(size, slice);
					slice += sliceSize;
				}
			}


			polyBoard.Sides = Math.Max(effectiveMaxCount * 6, minPolySize);
		}





		protected override void InputDepth()
		{
			_config[StateID] &= ~(uint)HudElementStates.IsMouseInBounds;

			if (HudMain.InputMode == HudInputMode.NoInput || !(HudSpace?.IsFacingCamera ?? false))
				return;

			Vector2 size = UnpaddedSize;
			Vector2 aspect = new Vector2(size.Y / size.X, size.X / size.Y);
			Vector2 cursorPos = new Vector2(HudSpace.CursorPos.X, HudSpace.CursorPos.Y) - Position;
			cursorPos *= aspect;

			float outerRadius = 0.5f * size.X;
			float innerRadius = polyBoard.InnerRadius * outerRadius;
			float distance = cursorPos.Length();


			if (distance > innerRadius && distance < outerRadius)
			{
				_config[StateID] |= (uint)HudElementStates.IsMouseInBounds;
				HudMain.Cursor.TryCaptureHudSpace(HudSpace.CursorPos.Z, HudSpace.GetHudSpaceFunc);
			}
		}





		protected override void HandleInput(Vector2 cursorPos)
		{
			if (UseGestureInput || IsMousedOver)
			{
				if (isStartPosStale)
				{
					cursorNormal = Vector2.Zero;
					lastCursorPos = cursorPos;
					isStartPosStale = false;
				}

				UpdateSelection(cursorPos);
			}
			else
			{
				isStartPosStale = true;
			}
		}





		protected virtual void UpdateSelection(Vector2 cursorPos)
		{
			Vector2 offset = UseGestureInput ? (cursorPos - lastCursorPos) : (cursorPos - Position);


			if (offset.LengthSquared() > 64f)
			{
				if (UseGestureInput)
				{

					Vector2 normalized = CursorSensitivity * 0.4f * Vector2.Normalize(offset);
					cursorNormal = Vector2.Normalize(cursorNormal + normalized);
				}
				else
				{
					cursorNormal = Vector2.Normalize(offset);
				}

				float bestDot = 0.5f;
				int bestIndex = -1;


				for (int i = 0; i < hudCollectionList.Count; i++)
				{
					var container = hudCollectionList[i];
					if (container.Enabled)
					{
						float dot = (float)Math.Round(Vector2.Dot(container.Element.Offset, cursorNormal), 4);
						if (dot > bestDot)
						{
							bestDot = dot;
							bestIndex = i;
						}
					}
				}

				HighlightIndex = bestIndex;
				lastCursorPos = cursorPos;
			}
		}






		protected void UpdateVisPos()
		{
			selectionVisPos = -1;
			highlightVisPos = -1;

			SelectionIndex = MathHelper.Clamp(SelectionIndex, -1, hudCollectionList.Count - 1);
			HighlightIndex = MathHelper.Clamp(HighlightIndex, -1, hudCollectionList.Count - 1);

			if (hudCollectionList.Count == 0)
				return;

			if (SelectionIndex != -1)
			{
				for (int i = 0; i <= SelectionIndex; i++)
					if (hudCollectionList[i].Enabled)
						selectionVisPos++;
			}

			if (HighlightIndex != -1)
			{
				for (int i = 0; i <= HighlightIndex; i++)
					if (hudCollectionList[i].Enabled)
						highlightVisPos++;
			}
		}





		protected override void Draw()
		{
			Vector2 size = UnpaddedSize;
			int sliceSize = polyBoard.Sides / effectiveMaxCount;

			polyBoard.Color = BackgroundColor;
			UpdateVisPos();
			polyBoard.Draw(size, Position, HudSpace.PlaneToWorldRef);

			if (sliceSize <= 0)
				return;


			if (selectionVisPos != -1 && (highlightVisPos != selectionVisPos || !UseGestureInput))
			{
				Vector2I slice = new Vector2I(0, sliceSize - 1) + (selectionVisPos * sliceSize);
				polyBoard.Color = SelectionColor;
				polyBoard.Draw(size, Position, slice, HudSpace.PlaneToWorldRef);
			}


			if (highlightVisPos != -1 && (highlightVisPos != selectionVisPos || UseGestureInput))
			{
				Vector2I slice = new Vector2I(0, sliceSize - 1) + (highlightVisPos * sliceSize);
				polyBoard.Color = HighlightColor;
				polyBoard.Draw(size, Position, slice, HudSpace.PlaneToWorldRef);
			}
		}
	}









	public class RadialSelectionBox : RadialSelectionBox<ScrollBoxEntry>
	{
		public RadialSelectionBox(HudParentBase parent = null) : base(parent) { }
	}









	public class RadialSelectionBox<TContainer> : RadialSelectionBox<TContainer, HudElementBase>
		where TContainer : IScrollBoxEntry<HudElementBase>, new()
	{
		public RadialSelectionBox(HudParentBase parent = null) : base(parent) { }
	}
}