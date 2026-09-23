using VRageMath;

namespace RichHudFramework
{
	namespace UI
	{
		using static RichHudFramework.UI.NodeConfigIndices;
		using Server;
		using Client;

		public abstract partial class HudElementBase : HudNodeBase, IReadOnlyHudElement
		{
			protected const float MinMouseBounds = 8f;

			public Vector2 Size
			{
				get { return UnpaddedSize + Padding; }
				set
				{
					if (value.X > Padding.X)
						value.X -= Padding.X;

					if (value.Y > Padding.Y)
						value.Y -= Padding.Y;

					UnpaddedSize = value;
				}
			}

			public float Width
			{
				get { return UnpaddedSize.X + Padding.X; }
				set
				{
					if (value > Padding.X)
						value -= Padding.X;


					UnpaddedSize = new Vector2(value, UnpaddedSize.Y);
				}
			}

			public float Height
			{
				get { return UnpaddedSize.Y + Padding.Y; }
				set
				{
					if (value > Padding.Y)
						value -= Padding.Y;


					UnpaddedSize = new Vector2(UnpaddedSize.X, value);
				}
			}

			public Vector2 Padding { get; set; }

			public Vector2 UnpaddedSize { get; set; }

			public Vector2 Origin { get; private set; }

			public Vector2 Offset { get; set; }

			public Vector2 Position { get; private set; }

			public ParentAlignments ParentAlignment { get; set; }

			public DimAlignments DimAlignment { get; set; }

			public bool UseCursor
			{

				get { return (Config[StateID] & (uint)HudElementStates.CanUseCursor) > 0; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.CanUseCursor;
					else
						_config[StateID] &= ~(uint)HudElementStates.CanUseCursor;

					if (value && _dataHandle[0].Item3.Item3 == null)
						_dataHandle[0].Item3.Item3 = BeginInput;
				}
			}

			public bool ShareCursor
			{

				get { return (Config[StateID] & (uint)HudElementStates.CanShareCursor) > 0; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.CanShareCursor;
					else
						_config[StateID] &= ~(uint)HudElementStates.CanShareCursor;
				}
			}

			public bool IsMasking
			{

				get { return (Config[StateID] & (uint)HudElementStates.IsMasking) > 0; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.IsMasking;
					else
						_config[StateID] &= ~(uint)HudElementStates.IsMasking;
				}
			}

			public bool IsSelectivelyMasked
			{

				get { return (Config[StateID] & (uint)HudElementStates.IsSelectivelyMasked) > 0; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.IsSelectivelyMasked;
					else
						_config[StateID] &= ~(uint)HudElementStates.IsSelectivelyMasked;
				}
			}

			public bool CanIgnoreMasking
			{

				get { return (Config[StateID] & (uint)HudElementStates.CanIgnoreMasking) > 0; }
				set
				{
					if (value)
						_config[StateID] |= (uint)HudElementStates.CanIgnoreMasking;
					else
						_config[StateID] &= ~(uint)HudElementStates.CanIgnoreMasking;
				}
			}

			public virtual bool IsMousedOver => (Config[StateID] & (uint)HudElementStates.IsMousedOver) > 0;

			protected Vector2 CachedSize { get; private set; }

			protected Vector2 OriginAlignment { get; private set; }

			protected BoundingBox2? MaskingBox { get; private set; }


			public HudElementBase(HudParentBase parent) : base(parent)
			{
				DimAlignment = DimAlignments.None;
				ParentAlignment = ParentAlignments.Center;

				Origin = Vector2.Zero;
				Position = Vector2.Zero;
				OriginAlignment = Vector2.Zero;
			}


			protected override void InputDepth()
			{
				if (HudSpace.IsFacingCamera)
				{
					Vector3 cursorPos = HudSpace.CursorPos;
					Vector2 halfSize = Vector2.Max(CachedSize, new Vector2(MinMouseBounds)) * .5f;

					BoundingBox2 box = new BoundingBox2(Position - halfSize, Position + halfSize);
					bool mouseInBounds;

					if (MaskingBox == null)
						mouseInBounds = box.Contains(new Vector2(cursorPos.X, cursorPos.Y)) == ContainmentType.Contains;
					else
						mouseInBounds = box.Intersect(MaskingBox.Value).Contains(new Vector2(cursorPos.X, cursorPos.Y)) == ContainmentType.Contains;

					if (mouseInBounds)
					{
						_config[StateID] |= (uint)HudElementStates.IsMouseInBounds;
						HudMain.Cursor.TryCaptureHudSpace(cursorPos.Z, HudSpace.GetHudSpaceFunc);
					}
				}
			}


			protected sealed override void BeginInput()
			{
				Vector3 cursorPos = HudSpace.CursorPos;
				bool canUseCursor = (Config[StateID] & (uint)HudElementStates.CanUseCursor) > 0,
					canShareCursor = (Config[StateID] & (uint)HudElementStates.CanShareCursor) > 0;
				bool mouseInBounds = (Config[StateID] & (uint)HudElementStates.IsMouseInBounds) > 0;

				if (canUseCursor && mouseInBounds && !HudMain.Cursor.IsCaptured && HudMain.Cursor.IsCapturingSpace(HudSpace.GetHudSpaceFunc))
				{
					bool isMousedOver = mouseInBounds;

					if (isMousedOver)
						_config[StateID] |= (uint)HudElementStates.IsMousedOver;

					if ((Config[StateID] & (uint)HudElementStates.IsInputHandlerCustom) > 0)
						HandleInput(new Vector2(cursorPos.X, cursorPos.Y));

					if (!canShareCursor)
						HudMain.Cursor.TryCapture(DataHandle[0].Item3.Item1);
				}
				else if ((Config[StateID] & (uint)HudElementStates.IsInputHandlerCustom) > 0)
				{
					HandleInput(new Vector2(cursorPos.X, cursorPos.Y));
				}
			}


			protected sealed override void BeginLayout(bool _)
			{
				var parentFull = Parent as HudElementBase;
				HudSpace = Parent?.HudSpace;

				if (HudSpace != null)
					_config[StateID] |= (uint)HudElementStates.IsSpaceNodeReady;
				else
					_config[StateID] &= ~(uint)HudElementStates.IsSpaceNodeReady;

				if (parentFull != null)
				{
					Origin = parentFull.Position + OriginAlignment;
				}
				else
				{
					Position = Origin + Offset;
					Padding = Padding;
					CachedSize = UnpaddedSize + Padding;
				}

				if ((Config[StateID] & (uint)HudElementStates.IsLayoutCustom) > 0)
					Layout();

				if (parentFull != null && (parentFull.Config[StateID] & (uint)HudElementStates.IsMasked) > 0 &&
					(Config[StateID] & (uint)HudElementStates.CanIgnoreMasking) == 0
				)
					_config[StateID] |= (uint)HudElementStates.IsMasked;
				else
					_config[StateID] &= ~(uint)HudElementStates.IsMasked;

				if ((Config[StateID] & (uint)HudElementStates.IsMasking) > 0 ||
					(parentFull != null && (Config[StateID] & (uint)HudElementStates.IsSelectivelyMasked) > 0))
				{
					UpdateMasking();
				}
				else if ((Config[StateID] & (uint)HudElementStates.IsMasked) > 0)
					MaskingBox = parentFull?.MaskingBox;
				else
					MaskingBox = null;

				bool isDisjoint = false;

				if ((Config[StateID] & (uint)HudElementStates.IsMasking) > 0 && MaskingBox != null)
				{
					Vector2 halfSize = CachedSize * .5f;

					var bounds = new BoundingBox2(Position - halfSize, Position + halfSize);
					isDisjoint =
						(bounds.Max.X < MaskingBox.Value.Min.X) ||
						(bounds.Min.X > MaskingBox.Value.Max.X) ||
						(bounds.Max.Y < MaskingBox.Value.Min.Y) ||
						(bounds.Min.Y > MaskingBox.Value.Max.Y);
				}

				if (isDisjoint)
					_config[StateID] |= (uint)HudElementStates.IsDisjoint;
				else
					_config[StateID] &= ~(uint)HudElementStates.IsDisjoint;

				if (children.Count > 0)
					UpdateChildAlignment();
			}			


			private void UpdateChildAlignment()
			{
				for (int i = 0; i < children.Count; i++)
				{
					var child = children[i] as HudElementBase;

					if (child == null)
						continue;

					child._config[StateID] |= (uint)HudElementStates.WasParentVisible;

					if (child != null && (child.Config[StateID] & (child.Config[VisMaskID])) == child.Config[VisMaskID])
					{
						Vector2 childSize = child.UnpaddedSize + child.Padding;
						DimAlignments sizeFlags = child.DimAlignment;

						if (sizeFlags != DimAlignments.None)
						{
							if ((sizeFlags & DimAlignments.IgnorePadding) == DimAlignments.IgnorePadding)
							{
								if ((sizeFlags & DimAlignments.Width) == DimAlignments.Width)
									childSize.X = UnpaddedSize.X;

								if ((sizeFlags & DimAlignments.Height) == DimAlignments.Height)
									childSize.Y = UnpaddedSize.Y;
							}
							else
							{
								if ((sizeFlags & DimAlignments.Width) == DimAlignments.Width)
									childSize.X = CachedSize.X;

								if ((sizeFlags & DimAlignments.Height) == DimAlignments.Height)
									childSize.Y = CachedSize.Y;
							}

							child.UnpaddedSize = childSize - child.Padding;
						}

						child.CachedSize = childSize;
					}
				}

				for (int i = 0; i < children.Count; i++)
				{
					var child = children[i] as HudElementBase;

					if (child != null && (child.Config[StateID] & (child.Config[VisMaskID])) == child.Config[VisMaskID])
					{
						ParentAlignments originFlags = child.ParentAlignment;
						Vector2 delta = Vector2.Zero,
							max = (CachedSize + child.CachedSize) * .5f,
							min = -max;

						if ((originFlags & ParentAlignments.UsePadding) == ParentAlignments.UsePadding)
						{
							min += Padding * .5f;
							max -= Padding * .5f;
						}

						if ((originFlags & ParentAlignments.InnerV) == ParentAlignments.InnerV)
						{
							min.Y += child.CachedSize.Y;
							max.Y -= child.CachedSize.Y;
						}

						if ((originFlags & ParentAlignments.InnerH) == ParentAlignments.InnerH)
						{
							min.X += child.CachedSize.X;
							max.X -= child.CachedSize.X;
						}

						if ((originFlags & ParentAlignments.Bottom) == ParentAlignments.Bottom)
							delta.Y = min.Y;
						else if ((originFlags & ParentAlignments.Top) == ParentAlignments.Top)
							delta.Y = max.Y;

						if ((originFlags & ParentAlignments.Left) == ParentAlignments.Left)
							delta.X = min.X;
						else if ((originFlags & ParentAlignments.Right) == ParentAlignments.Right)
							delta.X = max.X;

						child.OriginAlignment = delta;
						child.Origin = Position + delta;
						child.Position = child.Origin + child.Offset;
					}
				}
			}


			private void UpdateMasking()
			{
				_config[StateID] |= (uint)HudElementStates.IsMasked;

				BoundingBox2? parentBox, box = null;
				var parentFull = Parent as HudElementBase;

				if ((Config[StateID] & (uint)HudElementStates.CanIgnoreMasking) > 0)
				{
					parentBox = null;
				}
				else if (parentFull != null && (Config[StateID] & (uint)HudElementStates.IsSelectivelyMasked) > 0)
				{
					Vector2 halfParent = .5f * parentFull.CachedSize;

					parentBox = new BoundingBox2(
						-halfParent + parentFull.Position,
						halfParent + parentFull.Position
					);

					if (parentFull.MaskingBox != null)
						parentBox = parentBox.Value.Intersect(parentFull.MaskingBox.Value);
				}
				else
					parentBox = parentFull?.MaskingBox;

				if ((Config[StateID] & (uint)HudElementStates.IsMasking) > 0)
				{
					Vector2 halfSize = .5f * CachedSize;

					box = new BoundingBox2(
						-halfSize + Position,
						halfSize + Position
					);
				}

				if (parentBox != null && box != null)
					box = box.Value.Intersect(parentBox.Value);

				else if (box == null)
					box = parentBox;

				MaskingBox = box;
			}


			protected override object GetOrSetApiMember(object data, int memberEnum)
			{
				switch ((HudElementAccessors)memberEnum)
				{
					case HudElementAccessors.Position:
						return Position;
					case HudElementAccessors.Size:
						return Size;
				}

				return base.GetOrSetApiMember(data, memberEnum);
			}
		}
	}
}