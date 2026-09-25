using System;
using VRageMath;
using RichHudFramework.UI.Rendering;

namespace RichHudFramework.UI
{
	using Client;
	using Server;

	public abstract class WindowBase : HudElementBase, IClickableElement
	{
		public RichText HeaderText { get { return HeaderBuilder.GetText(); } set { HeaderBuilder.SetText(value); } }

		public ITextBuilder HeaderBuilder => header.TextBoard;

		public virtual Color BorderColor
		{
			get { return header.Color; }
			set
			{
				header.Color = value;
				border.Color = value;
			}
		}

		public virtual Color BodyColor { get { return windowBg.Color; } set { windowBg.Color = value; } }

		public Vector2 MinimumSize { get; set; }

		public bool AllowResizing { get; set; }

		public bool CanDrag { get; set; }

		public bool WindowActive { get; protected set; }

		public override bool IsMousedOver => resizeInput.IsMousedOver;

		public IMouseInput MouseInput { get; }

		public IFocusHandler FocusHandler { get; }

		public readonly LabelBoxButton header;

		public readonly HudElementBase body;

		public readonly BorderBox border;

		protected readonly MouseInputElement inputInner, resizeInput;
		protected readonly TexturedBox windowBg;

		protected float cornerSize = 16f;
		protected bool canMoveWindow;
		protected Vector2 resizeDir, cursorOffset;


		public WindowBase(HudParentBase parent) : base(parent)
		{

			header = new LabelBoxButton(this)
			{
				DimAlignment = DimAlignments.Width,
				Height = 32f,
				ParentAlignment = ParentAlignments.InnerTop,
				ZOffset = 1,
				Format = GlyphFormat.White.WithAlignment(TextAlignment.Center),
				HighlightEnabled = false,
				AutoResize = false,
			};


			body = new EmptyHudElement(this)
			{
				ParentAlignment = ParentAlignments.InnerBottom,
			};


			windowBg = new TexturedBox(this)
			{
				DimAlignment = DimAlignments.Size,
				ZOffset = -2,
			};


			border = new BorderBox(this)
			{
				ZOffset = 1,
				Thickness = 1f,
				DimAlignment = DimAlignments.Size,
			};


			FocusHandler = new InputFocusHandler(this);

			resizeInput = new MouseInputElement(this)
			{
				ZOffset = sbyte.MaxValue,

				Padding = new Vector2(16f),
				DimAlignment = DimAlignments.Size,
				CanIgnoreMasking = true
			};

			inputInner = new MouseInputElement(resizeInput)
			{
				DimAlignment = DimAlignments.UnpaddedSize,
			};

			resizeDir = Vector2.Zero;
			AllowResizing = true;
			CanDrag = true;
			UseCursor = true;
			ShareCursor = false;
			IsMasking = true;

			MinimumSize = new Vector2(200f, 200f);
			MouseInput = resizeInput;

			GetWindowFocus();
		}


		protected override void Layout()
		{
			body.Height = UnpaddedSize.Y - header.Height;
			body.Width = UnpaddedSize.X;
		}


		protected void Resize(Vector2 cursorPos)
		{
			Vector2 pos = Origin + Offset,
				delta = resizeDir * (cursorPos - pos),
				size = CachedSize;

			if (delta.X > 0f)
			{
				delta.X = Math.Max(delta.X, .5f * MinimumSize.X);
				size.X = .5f * size.X + delta.X;
				pos.X = ((resizeDir.X * delta.X) + pos.X) + (-resizeDir.X * .5f * size.X);
			}

			if (delta.Y > 0f)
			{
				delta.Y = Math.Max(delta.Y, .5f * MinimumSize.Y);
				size.Y = .5f * size.Y + delta.Y;
				pos.Y = ((resizeDir.Y * delta.Y) + pos.Y) + (-resizeDir.Y * .5f * size.Y);
			}

			Size = size;
			Offset = pos - Origin;
		}


		protected override void HandleInput(Vector2 cursorPos)
		{
			if (IsMousedOver)
			{
				if (SharedBinds.LeftButton.IsNewPressed && !WindowActive)
					GetWindowFocus();
			}

			if (AllowResizing && resizeInput.IsNewLeftClicked && !inputInner.IsMousedOver)
			{
				Vector2 pos = Origin + Offset,
						delta = cursorPos - pos;

				resizeDir = Vector2.Zero;

				if (Width - (2f * Math.Abs(delta.X)) <= cornerSize)
					resizeDir.X = (delta.X >= 0f) ? 1f : -1f;

				if (Height - (2f * Math.Abs(delta.Y)) <= cornerSize)
					resizeDir.Y = (delta.Y >= 0f) ? 1f : -1f;
			}

			else if (CanDrag && header.MouseInput.IsNewLeftClicked)
			{
				canMoveWindow = true;
				cursorOffset = (Origin + Offset) - cursorPos;
			}

			if ((resizeDir != Vector2.Zero) || canMoveWindow)
			{
				if (!SharedBinds.LeftButton.IsPressed)
				{
					canMoveWindow = false;
					resizeDir = Vector2.Zero;
				}
			}

			if (!WindowActive)
			{
				canMoveWindow = false;
				resizeDir = Vector2.Zero;
			}

			if (canMoveWindow)
				Offset = cursorPos + cursorOffset - Origin;

			if (resizeDir != Vector2.Zero)
				Resize(cursorPos);
		}


		public virtual void GetWindowFocus()
		{
			OverlayOffset = HudMain.GetFocusOffset(LoseWindowFocus);
			WindowActive = true;
		}


		protected virtual void LoseWindowFocus(byte newLayer)
		{
			OverlayOffset = newLayer;
			WindowActive = false;
		}
	}
}