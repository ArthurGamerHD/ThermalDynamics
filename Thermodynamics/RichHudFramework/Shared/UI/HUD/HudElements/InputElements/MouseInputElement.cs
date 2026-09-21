using System;
using VRageMath;

namespace RichHudFramework.UI
{
	using Client;
	using Server;
	using static NodeConfigIndices;

	public class MouseInputElement : HudElementBase, IMouseInput
	{
		public IFocusHandler FocusHandler { get; protected set; }

		public event EventHandler CursorEntered;

		public event EventHandler CursorExited;

		public event EventHandler LeftClicked;

		public event EventHandler LeftReleased;

		public event EventHandler RightClicked;

		public event EventHandler RightReleased;

		public EventHandler CursorEnteredCallback { set { CursorEntered += value; } }

		public EventHandler CursorExitedCallback { set { CursorExited += value; } }
		public EventHandler LeftClickedCallback { set { LeftClicked += value; } }

		public EventHandler LeftReleasedCallback { set { LeftReleased += value; } }

		public EventHandler RightClickedCallback { set { RightClicked += value; } }

		public EventHandler RightReleasedCallback { set { RightReleased += value; } }

        public bool RequestCursor { get; set; }

        public ToolTip ToolTip { get; set; }

		public bool IsLeftClicked { get; private set; }

		public bool IsRightClicked { get; private set; }

		public bool IsNewLeftClicked { get; private set; }

		public bool IsNewRightClicked { get; private set; }

		public bool IsLeftReleased { get; private set; }

		public bool IsRightReleased { get; private set; }

		private bool mouseCursorEntered;

/// <summary>MouseInputElement operation.</summary>
		public MouseInputElement(HudParentBase parent) : base(parent)
		{
			FocusHandler = (parent as IFocusableElement)?.FocusHandler;
			UseCursor = true;
			ShareCursor = true;
			DimAlignment = DimAlignments.UnpaddedSize;
		}

/// <summary>MouseInputElement operation.</summary>
		public MouseInputElement() : this(null)
		{ }

/// <summary>ClearSubscribers operation.</summary>
		public void ClearSubscribers()
		{
			CursorEntered = null;
			CursorExited = null;
			LeftClicked = null;
			LeftReleased = null;
			RightClicked = null;
			RightReleased = null;
		}

/// <summary>InputDepth operation.</summary>
		protected override void InputDepth()
		{
			if (HudSpace.IsFacingCamera)
			{
				Vector3 cursorPos = HudSpace.CursorPos;
				Vector2 halfSize = Vector2.Max(CachedSize, new Vector2(MinMouseBounds)) * .5f;
/// <summary>BoundingBox2 operation.</summary>
				BoundingBox2 box = new BoundingBox2(Position - halfSize, Position + halfSize);
				bool mouseInBounds;

				if (MaskingBox == null)
				{
					mouseInBounds = box.Contains(new Vector2(cursorPos.X, cursorPos.Y)) == ContainmentType.Contains
						|| (IsLeftClicked || IsRightClicked);
				}
				else
				{
					mouseInBounds = box.Intersect(MaskingBox.Value).Contains(new Vector2(cursorPos.X, cursorPos.Y)) == ContainmentType.Contains
						|| (IsLeftClicked || IsRightClicked);
				}

				if (mouseInBounds)
				{
					_config[StateID] |= (uint)HudElementStates.IsMouseInBounds;
					HudMain.Cursor.TryCaptureHudSpace(cursorPos.Z, HudSpace.GetHudSpaceFunc);
				}
			}
		}

/// <summary>HandleInput operation.</summary>
		protected override void HandleInput(Vector2 cursorPos)
		{
			FocusHandler = (Parent as IFocusableElement)?.FocusHandler;
            var owner = (object)(FocusHandler?.InputOwner) ?? Parent;

			if (RequestCursor)
				HudMain.EnableCursorTemp();

            if (IsMousedOver)
			{
				if (!mouseCursorEntered)
				{
					mouseCursorEntered = true;
					CursorEntered?.Invoke(owner, EventArgs.Empty);
				}

				if (SharedBinds.LeftButton.IsNewPressed)
				{
					FocusHandler?.GetInputFocus();
					LeftClick();
				}
				else
					IsNewLeftClicked = false;

				if (SharedBinds.RightButton.IsNewPressed)
				{
					FocusHandler?.GetInputFocus();
					RightClick();
				}
				else
					IsNewRightClicked = false;

				if (ToolTip != null)
					HudMain.Cursor.RegisterToolTip(ToolTip);
			}
			else
			{
				if (mouseCursorEntered)
				{
					mouseCursorEntered = false;
					CursorExited?.Invoke(owner, EventArgs.Empty);
				}

				bool hasFocus = FocusHandler?.HasFocus ?? false;

				if (hasFocus && (SharedBinds.LeftButton.IsNewPressed || SharedBinds.RightButton.IsNewPressed))
					FocusHandler.ReleaseFocus();

				IsNewLeftClicked = false;
				IsNewRightClicked = false;
			}

			if (!SharedBinds.LeftButton.IsPressed && IsLeftClicked)
			{
				LeftReleased?.Invoke(owner, EventArgs.Empty);
				IsLeftReleased = true;
				IsLeftClicked = false;
			}
			else
				IsLeftReleased = false;

			if (!SharedBinds.RightButton.IsPressed && IsRightClicked)
			{
				RightReleased?.Invoke(owner, EventArgs.Empty);
				IsRightReleased = true;
				IsRightClicked = false;
			}
			else
				IsRightReleased = false;
		}

/// <summary>LeftClick operation.</summary>
		public virtual void LeftClick()
		{
            var owner = (object)(FocusHandler?.InputOwner) ?? Parent;
            LeftClicked?.Invoke(owner, EventArgs.Empty);
			IsLeftClicked = true;
			IsNewLeftClicked = true;
			IsLeftReleased = false;
		}

/// <summary>RightClick operation.</summary>
		public virtual void RightClick()
		{
            var owner = (object)(FocusHandler?.InputOwner) ?? Parent;
            RightClicked?.Invoke(owner, EventArgs.Empty);
			IsRightClicked = true;
			IsNewRightClicked = true;
			IsRightReleased = false;
		}
	}
}