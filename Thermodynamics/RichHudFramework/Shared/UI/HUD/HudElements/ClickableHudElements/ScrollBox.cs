using System;
using VRageMath;

namespace RichHudFramework.UI
{
    using static NodeConfigIndices;

    public class ScrollBox<TElementContainer, TElement> : HudChain<TElementContainer, TElement>

        where TElementContainer : IScrollBoxEntry<TElement>, new()
        where TElement : HudElementBase
    {
        public int MinVisibleCount { get; set; }

        public float MinLength { get; set; }

        public int Start
        {
            get { return MathHelper.Clamp(_intStart, 0, hudCollectionList.Count - 1); }
            set
            {
                if (value != _intStart)
                {
                    _intStart = MathHelper.Clamp(value, 0, hudCollectionList.Count - 1);

                    ScrollBar.Value = GetMinScrollOffset(_intStart, false);
                }
            }
        }

        public int End
        {
            get { return MathHelper.Clamp(_intEnd, 0, hudCollectionList.Count - 1); }
            set
            {
                if (value != _intEnd)
                {
                    _intEnd = MathHelper.Clamp(value, 0, hudCollectionList.Count - 1);

                    ScrollBar.Value = GetMinScrollOffset(_intEnd, true);
                }
            }
        }


        public Vector2I ClipRange => new Vector2I(_start, _end);

        public int VisStart { get; private set; }

        public int VisCount { get; private set; }

        public int EnabledCount { get; private set; }

        public Color Color { get { return Background.Color; } set { Background.Color = value; } }

        public Color BarColor { get { return ScrollBar.SlideInput.BarColor; } set { ScrollBar.SlideInput.BarColor = value; } }

        public Color BarHighlight { get { return ScrollBar.SlideInput.BarHighlight; } set { ScrollBar.SlideInput.BarHighlight = value; } }

        public Color SliderColor { get { return ScrollBar.SlideInput.SliderColor; } set { ScrollBar.SlideInput.SliderColor = value; } }

        public Color SliderHighlight { get { return ScrollBar.SlideInput.SliderHighlight; } set { ScrollBar.SlideInput.SliderHighlight = value; } }

        public bool EnableScrolling { get; set; }

        public bool UseSmoothScrolling { get; set; }

        public override bool AlignVertical
        {
            set
            {
                if (ScrollBar == null)

                    ScrollBar = new ScrollBar(this);

                if (Divider == null)

                    Divider = new TexturedBox(ScrollBar) { Color = new Color(53, 66, 75) };

                ScrollBar.Vertical = value;
                base.AlignVertical = value;

                if (value)
                {
                    ScrollBar.DimAlignment = DimAlignments.Height;
                    Divider.DimAlignment = DimAlignments.Height;

                    ScrollBar.ParentAlignment = ParentAlignments.InnerRight;
                    Divider.ParentAlignment = ParentAlignments.InnerLeft;


                    Divider.Padding = new Vector2(2f, 0f);
                    Divider.Width = 1f;


                    ScrollBar.Padding = new Vector2(30f, 10f);
                    ScrollBar.Width = 43f;
                }
                else
                {
                    ScrollBar.DimAlignment = DimAlignments.Width;
                    Divider.DimAlignment = DimAlignments.Width;

                    ScrollBar.ParentAlignment = ParentAlignments.InnerBottom;
                    Divider.ParentAlignment = ParentAlignments.InnerBottom;


                    Divider.Padding = new Vector2(16f, 2f);
                    Divider.Height = 1f;


                    ScrollBar.Padding = new Vector2(16f);
                    ScrollBar.Height = 24f;
                }
            }
        }

        public ScrollBar ScrollBar { get; protected set; }

        public TexturedBox Divider { get; protected set; }

        public TexturedBox Background { get; protected set; }

        private float scrollBarPadding;
        private int _intStart;
        private int _intEnd;
        private int _start;
        private int _end;

        private int firstEnabled;


        public ScrollBox(bool alignVertical, HudParentBase parent = null) : base(alignVertical, parent)
        {

            Background = new TexturedBox(this)
            {
                Color = TerminalFormatting.DarkSlateGrey,
                DimAlignment = DimAlignments.Size,
                ZOffset = -1,
            };

            UseCursor = true;
            ShareCursor = false;
            EnableScrolling = true;
            UseSmoothScrolling = true;
            ZOffset = 1;
            AlignVertical = alignVertical;

            MinVisibleCount = 0;
            MinLength = 0f;
            SizingMode = HudChainSizingModes.FitChainOffAxis;
        }


        public ScrollBox(HudParentBase parent) : this(true, parent)
        { }


        public ScrollBox() : this(true, null)
        { }


        public override Vector2 GetRangeSize(int start = 0, int end = -1)
        {
            Vector2 size = base.GetRangeSize(start, end);
            size[offAxis] += scrollBarPadding;
            return size;
        }


        public int GetRangeEnd(int count, int start = 0)
        {
            start = MathHelper.Clamp(start, 0, hudCollectionList.Count - 1);
            count = MathHelper.Clamp(count, 0, hudCollectionList.Count - start);

            if ((start + count) <= hudCollectionList.Count)
            {
                int end = start,
                    enCount = 0;

                for (int i = start; i < hudCollectionList.Count; i++)
                {
                    if (hudCollectionList[i].Enabled)
                    {
                        end = i;
                        enCount++;
                    }

                    if (enCount >= count)
                        break;
                }

                return end;
            }

            return -1;
        }


        protected override void HandleInput(Vector2 cursorPos)
        {
            ScrollBar.InputEnabled = EnableScrolling;
            ShareCursor = ScrollBar.Max <= 0f;

            if (hudCollectionList.Count > 0 && EnableScrolling && (IsMousedOver || ScrollBar.IsMousedOver))
            {
                if (UseSmoothScrolling)
                {
                    if (SharedBinds.MousewheelUp.IsPressed)
                        ScrollBar.Value -= hudCollectionList[_intEnd].Element.Size[alignAxis] + Spacing;

                    else if (SharedBinds.MousewheelDown.IsPressed)
                        ScrollBar.Value += hudCollectionList[_intStart].Element.Size[alignAxis] + Spacing;
                }
                else
                {
                    if (SharedBinds.MousewheelUp.IsPressed)
                        Start--;

                    else if (SharedBinds.MousewheelDown.IsPressed)
                        End++;
                }
            }
        }


        protected override Vector2 GetBoundedRangeSize()
        {
            Vector2 listSize = Vector2.Zero,
                minSize = MemberMinSize,
                maxSize = MemberMaxSize;
            int visCount = 0;

            maxSize[offAxis] = (maxSize[offAxis] == 0f) ? UnpaddedSize[offAxis] : maxSize[offAxis];

            for (int i = _intStart; i < hudCollectionList.Count; i++)
            {
                var entry = hudCollectionList[i];
                TElement element = entry.Element;

                if (entry.Enabled)
                {
                    if ((MinVisibleCount != 0 || MinLength != 0) &&
                        (MinVisibleCount == 0 || visCount >= MinVisibleCount) &&
                        (MinLength == 0 || listSize[alignAxis] >= MinLength))
                    { break; }

                    Vector2 size = element.UnpaddedSize + element.Padding;

                    if ((SizingMode & HudChainSizingModes.FitMembersAlignAxis) > 0)
                        size[alignAxis] = (maxSize[alignAxis] > 0f) ? maxSize[alignAxis] : size[alignAxis];
                    else if ((SizingMode & HudChainSizingModes.ClampMembersAlignAxis) > 0)
                    {
                        if (maxSize[alignAxis] > 0f)
                            size[alignAxis] = MathHelper.Clamp(size[alignAxis], minSize[alignAxis], maxSize[alignAxis]);
                        else
                            size[alignAxis] = Math.Max(size[alignAxis], minSize[alignAxis]);
                    }

                    if ((SizingMode & HudChainSizingModes.FitMembersOffAxis) > 0)
                        size[offAxis] = (maxSize[offAxis] > 0f) ? maxSize[offAxis] : size[offAxis];
                    else if ((SizingMode & HudChainSizingModes.ClampMembersOffAxis) > 0)
                    {
                        if (maxSize[offAxis] > 0f)
                            size[offAxis] = MathHelper.Clamp(size[offAxis], minSize[offAxis], maxSize[offAxis]);
                        else
                            size[offAxis] = Math.Max(size[offAxis], minSize[offAxis]);
                    }

                    listSize[offAxis] = Math.Max(listSize[offAxis], size[offAxis]);
                    listSize[alignAxis] += size[alignAxis];
                    visCount++;
                }
            }

            listSize[alignAxis] += Spacing * (visCount - 1);
            return listSize;
        }


        protected override void Measure()
        {
            if (UseSmoothScrolling)
                _config[StateID] |= (uint)HudElementStates.IsMasking;

            if ((SizingMode & chainAutoAlignAxisMask) == 0 && (MinVisibleCount > 0 || MinLength > 0))
                SizingMode |= HudChainSizingModes.FitChainAlignAxis;

            if ((SizingMode & chainSelfSizingMask) > 0 || (UnpaddedSize.X == 0f || UnpaddedSize.Y == 0f))
            {
                Vector2 listSize = Vector2.Zero;

                if (hudCollectionList.Count > 0)

                    listSize = GetBoundedRangeSize();

                listSize[offAxis] += scrollBarPadding;
                Vector2 chainBounds = UnpaddedSize;

                if (listSize[alignAxis] > 0f)
                {
                    if (chainBounds[alignAxis] == 0f || (SizingMode & HudChainSizingModes.FitChainAlignAxis) > 0)
                        chainBounds[alignAxis] = listSize[alignAxis];
                    else if ((SizingMode & HudChainSizingModes.ClampChainAlignAxis) == HudChainSizingModes.ClampChainAlignAxis)
                        chainBounds[alignAxis] = Math.Max(chainBounds[alignAxis], listSize[alignAxis]);
                }

                if (listSize[offAxis] > 0f)
                {
                    if (chainBounds[offAxis] == 0f || (SizingMode & HudChainSizingModes.FitChainOffAxis) > 0)
                        chainBounds[offAxis] = listSize[offAxis];
                    else if ((SizingMode & HudChainSizingModes.ClampChainOffAxis) == HudChainSizingModes.ClampChainOffAxis)
                        chainBounds[offAxis] = Math.Max(chainBounds[offAxis], listSize[offAxis]);
                }

                UnpaddedSize = chainBounds;
            }
        }


        protected override void Layout()
        {
            Vector2 effectivePadding = Padding;
            scrollBarPadding = ScrollBar.Size[offAxis];
            effectivePadding[offAxis] += scrollBarPadding;

            Vector2 chainSize = (UnpaddedSize + Padding) - effectivePadding;
            float sliderVisRatio = 0f;

            if (hudCollectionList.Count > 0)
            {
                float totalEnabledLength, scrollOffset,
                    rangeLength = chainSize[alignAxis];

                if (UseSmoothScrolling)
                {
                    UpdateSmoothRange(rangeLength, out totalEnabledLength, out scrollOffset);
                }
                else
                {
                    UpdateNormalRange(rangeLength, out totalEnabledLength);
                    scrollOffset = 0f;
                }

                UpdateRangeSize(chainSize);

                if (rangeLength > 0)
                {
                    Vector2 startOffset, endOffset;
                    float rcpSpanLength = 1f / Math.Max(rangeSize[alignAxis], 1E-6f);

                    if (alignAxis == 1)
                    {

                        startOffset = new Vector2(-.5f * scrollBarPadding, .5f * chainSize.Y + scrollOffset);

                        endOffset = new Vector2(startOffset.X, startOffset.Y - rangeSize[alignAxis]);
                    }
                    else
                    {

                        startOffset = new Vector2(-.5f * chainSize.X - scrollOffset, .5f * scrollBarPadding);

                        endOffset = new Vector2(startOffset.X + rangeSize[alignAxis], startOffset.Y);
                    }

                    UpdateMemberOffsets(startOffset, endOffset, rcpSpanLength, 0.5f * scrollBarPadding);

                    sliderVisRatio = chainSize[alignAxis] / totalEnabledLength;
                }
            }

            ScrollBar.VisiblePercent = sliderVisRatio;
        }


        private void UpdateSmoothRange(float maxLength, out float totalEnabledLength, out float scrollOffset)
        {
            EnabledCount = 0;
            firstEnabled = -1;
            totalEnabledLength = 0f;

            for (int i = 0; i < hudCollectionList.Count; i++)
            {
                if (hudCollectionList[i].Enabled)
                {
                    if (firstEnabled == -1)
                        firstEnabled = i;

                    TElement element = hudCollectionList[i].Element;
                    float elementSize = element.UnpaddedSize[alignAxis] + element.Padding[alignAxis];

                    totalEnabledLength += elementSize;
                    EnabledCount++;
                }
            }

            if (EnabledCount > 1)
            totalEnabledLength += (EnabledCount - 1) * Spacing;
            ScrollBar.Percent = (float)Math.Round(ScrollBar.Percent, 6);
            ScrollBar.Max = (float)Math.Round(Math.Max(totalEnabledLength - maxLength, 0f), 6);

            float scrollCurrent = ScrollBar.Value,
                epsilon = 1E-3f,
                viewTop = scrollCurrent,
                viewBottom = scrollCurrent + maxLength - epsilon;

            _intStart = -1;
            _intEnd = -1;
            VisCount = 0;
            scrollOffset = 0f;
            float currentPos = 0f;

            for (int i = 0; i < hudCollectionList.Count; i++)
            {
                if (hudCollectionList[i].Enabled)
                {
                    TElement element = hudCollectionList[i].Element;
                    float elementSize = element.UnpaddedSize[alignAxis] + element.Padding[alignAxis];
                    float elementTop = currentPos;
                    float elementBottom = currentPos + elementSize;

                    bool isInRange = (elementBottom > viewTop) && (elementTop < viewBottom);

                    if (isInRange)
                    {
                        if (_intStart == -1)
                        {
                            _intStart = i;
                            scrollOffset = elementTop - scrollCurrent;
                        }

                        _intEnd = i;
                        VisCount++;
                    }

                    else if (_intStart != -1)
                        break;

                    currentPos += elementSize + Spacing;
                }
            }

            int max = hudCollectionList.Count - 1;

            if (firstEnabled == -1)
            {
                _intStart = 0;
                _intEnd = 0;
                VisStart = 0;
                scrollOffset = 0f;
                return;
            }

            if (_intStart == -1)
            {
                _intStart = firstEnabled;
                _intEnd = firstEnabled;
            }

            scrollOffset *= -1f;
            _intStart = MathHelper.Clamp(_intStart, firstEnabled, max);
            _intEnd = MathHelper.Clamp(_intEnd, _intStart, max);
            _start = _intStart;
            _end = _intEnd;

            for (int i = _start - 1; i >= firstEnabled; i--)
            {
                if (hudCollectionList[i].Enabled)
                { _start = i; break; }
            }

            for (int i = _end + 1; i < hudCollectionList.Count; i++)
            {
                if (hudCollectionList[i].Enabled)
                { _end = i; break; }
            }

            if (_start != _intStart)
                scrollOffset += hudCollectionList[_start].Element.Size[alignAxis] + Spacing;


            VisStart = GetVisibleIndex(_intStart);

            for (int i = 0; i < hudCollectionList.Count; i++)
            {
                var element = hudCollectionList[i].Element;
                bool isInRange = (i >= _start && i <= _end) && hudCollectionList[i].Enabled;
                bool isVisible = (element.Config[StateID] & (uint)HudElementStates.IsVisible) > 0;

                if (isVisible != isInRange)
                    element.Visible = isInRange;
            }
        }


        private void UpdateNormalRange(float maxLength, out float totalEnabledLength)
        {
            EnabledCount = 0;
            firstEnabled = -1;
            totalEnabledLength = 0f;

            for (int i = 0; i < hudCollectionList.Count; i++)
            {
                if (hudCollectionList[i].Enabled)
                {
                    if (firstEnabled == -1) 
                        firstEnabled = i;

                    TElement element = hudCollectionList[i].Element;
                    totalEnabledLength += element.UnpaddedSize[alignAxis] + element.Padding[alignAxis];
                    EnabledCount++;
                }
            }

            if (EnabledCount > 1)
                totalEnabledLength += (EnabledCount - 1) * Spacing;

            ScrollBar.Max = (float)Math.Round(Math.Max(totalEnabledLength - maxLength, 0f), 5);

            _intEnd = -1;
            _intStart = -1;

            float currentScroll = ScrollBar.Value;
            float relativePos = (float)Math.Round(-currentScroll - maxLength, 5);

            for (int i = 0; i < hudCollectionList.Count; i++)
            {
                if (!hudCollectionList[i].Enabled) 
                    continue;

                TElement element = hudCollectionList[i].Element;
                relativePos += element.UnpaddedSize[alignAxis] + element.Padding[alignAxis];

                if (relativePos <= 0f)
                    _intEnd = i;
                else
                    break;

                relativePos += Spacing;
            }

            int maxIndex = hudCollectionList.Count - 1;
            firstEnabled = MathHelper.Clamp(firstEnabled, 0, maxIndex);
            _intEnd = MathHelper.Clamp(_intEnd, firstEnabled, maxIndex);

            _intStart = _intEnd;
            VisCount = 0;
            float availableSpace = maxLength;

            for (int i = _intEnd; i >= firstEnabled; i--)
            {
                if (!hudCollectionList[i].Enabled) 
                    continue;

                TElement element = hudCollectionList[i].Element;
                float size = element.UnpaddedSize[alignAxis] + element.Padding[alignAxis];

                if (availableSpace >= size)
                {
                    _intStart = i;
                    VisCount++;
                    availableSpace -= (size + Spacing);
                }
                else
                    break;
            }

            _start = _intStart;
            _end = _intEnd;

            VisStart = GetVisibleIndex(_intStart);

            for (int i = 0; i < hudCollectionList.Count; i++)
            {
                var element = hudCollectionList[i].Element;
                bool inVisibleRange = (i >= _start && i <= _end);
                bool shouldBeVisible = hudCollectionList[i].Enabled && inVisibleRange;
                bool isCurrentlyVisible = (element.Config[StateID] & (uint)HudElementStates.IsVisible) > 0;

                if (isCurrentlyVisible != shouldBeVisible)
                    element.Visible = shouldBeVisible;
            }
        }


        private int GetVisibleIndex(int index)
        {
            int count = 0;

            for (int n = 0; n < index; n++)
            {
                if (hudCollectionList[n].Enabled)
                    count++;
            }

            return count;
        }


        private float GetMinScrollOffset(int index, bool getEnd)
        {
            if (hudCollectionList.Count > 0)
            {
                firstEnabled = MathHelper.Clamp(firstEnabled, 0, hudCollectionList.Count - 1);
                float elementSize, topStart = 0f;

                if (getEnd)
                    topStart -= UnpaddedSize[alignAxis] + Spacing;
                else
                    index--;

                for (int i = 0; i <= index && i < hudCollectionList.Count; i++)
                {
                    if (hudCollectionList[i].Enabled)
                    {
                        TElement element = hudCollectionList[i].Element;
                        elementSize = element.UnpaddedSize[alignAxis] + element.Padding[alignAxis];
                        topStart += (elementSize + Spacing);
                    }
                }

                return Math.Max((float)Math.Round(topStart, 6), 0f);
            }
            else
                return 0f;
        }
    }

    public class ScrollBox<TElementContainer> : ScrollBox<TElementContainer, HudElementBase>

        where TElementContainer : IScrollBoxEntry<HudElementBase>, new()
    {

        public ScrollBox(bool alignVertical, HudParentBase parent = null) : base(alignVertical, parent)
        { }


        public ScrollBox(HudParentBase parent = null) : base(parent)
        { }
    }

    public class ScrollBox : ScrollBox<ScrollBoxEntry>
    {

        public ScrollBox(bool alignVertical, HudParentBase parent = null) : base(alignVertical, parent)
        { }


        public ScrollBox(HudParentBase parent = null) : base(parent)
        { }
    }
}