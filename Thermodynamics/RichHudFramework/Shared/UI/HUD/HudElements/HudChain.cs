using System;
using VRageMath;

namespace RichHudFramework
{
    namespace UI
    {
        using static NodeConfigIndices;

        [Flags]
        public enum HudChainSizingModes : ushort
        {

            None = 0,

            ClampChainOffAxis = 1 << 2,

            ClampChainAlignAxis = 1 << 3,

            ClampChainBoth = ClampChainOffAxis | ClampChainAlignAxis,

            FitChainOffAxis = 1 << 4,

            FitChainAlignAxis = 1 << 5,

            FitChainBoth = FitChainOffAxis | FitChainAlignAxis,


            ClampMembersOffAxis = 1 << 6,

            ClampMembersAlignAxis = 1 << 7,

            ClampMembersBoth = ClampMembersAlignAxis | ClampMembersOffAxis,

            FitMembersOffAxis = 1 << 8,

            FitMembersAlignAxis = 1 << 9,

            FitMembersBoth = FitMembersAlignAxis | FitMembersOffAxis,


            AlignMembersStart = 1 << 10,

            AlignMembersEnd = 1 << 11,

            AlignMembersCenter = 1 << 12,
        }

        public class HudChain<TElementContainer, TElement> : HudCollection<TElementContainer, TElement>

            where TElementContainer : IChainElementContainer<TElement>, new()
            where TElement : HudElementBase
        {
            protected const HudElementStates nodeSetVisible = HudElementStates.IsVisible | HudElementStates.IsRegistered;
            protected const HudChainSizingModes chainSelfSizingMask = HudChainSizingModes.FitChainBoth | HudChainSizingModes.ClampChainBoth;
            protected const HudChainSizingModes chainAutoAlignAxisMask = HudChainSizingModes.FitChainAlignAxis | HudChainSizingModes.ClampChainAlignAxis;
            protected const HudChainSizingModes chainAutoOffAxisMask = HudChainSizingModes.FitChainOffAxis | HudChainSizingModes.ClampChainOffAxis;
            protected const HudChainSizingModes memberVariableSizeMask = HudChainSizingModes.FitMembersBoth | HudChainSizingModes.ClampMembersBoth;
            protected const HudChainSizingModes memberVariableAlignAxisMask = HudChainSizingModes.FitMembersAlignAxis | HudChainSizingModes.ClampMembersAlignAxis;
            protected const HudChainSizingModes memberVariableOffAxisMask = HudChainSizingModes.FitMembersOffAxis | HudChainSizingModes.ClampMembersOffAxis;

            public new HudChain<TElementContainer, TElement> CollectionContainer => this;

            public float Spacing { get; set; }

            public Vector2 MemberMaxSize { get; set; }

            public Vector2 MemberMinSize { get; set; }

            public HudChainSizingModes SizingMode { get; set; }

            public virtual bool AlignVertical
            {
                get { return _alignVertical; }
                set
                {
                    if (value)
                    {
                        alignAxis = 1;
                        offAxis = 0;
                    }
                    else
                    {
                        alignAxis = 0;
                        offAxis = 1;
                    }

                    _alignVertical = value;
                }
            }

            protected bool _alignVertical;

            protected int alignAxis;

            protected int offAxis;

            protected Vector2 rangeSize;

            protected int rangeLength;


            public HudChain(bool alignVertical = false, HudParentBase parent = null) : base(parent)
            {
                Spacing = 0f;
                SizingMode = HudChainSizingModes.FitChainBoth;
                AlignVertical = alignVertical;
            }


            public HudChain(HudParentBase parent) : this(false, parent)
            { }


            public HudChain() : this(false, null)
            { }


            public virtual void Add(TElement element, float alignAxisScale)
            {

                var newContainer = new TElementContainer();
                newContainer.SetElement(element);
                newContainer.AlignAxisScale = alignAxisScale;
                Add(newContainer);
            }


            public virtual Vector2 SetRangeSize(Vector2 newSize, int start = 0, int end = -1)
            {
                Vector2 listSize = Vector2.Zero;
                int visCount = 0;

                if (hudCollectionList.Count > 0)
                {
                    if (end == -1)
                        end = hudCollectionList.Count - 1;

                    for (int i = start; i <= end; i++)
                    {
                        TElement element = hudCollectionList[i].Element;

                        if ((element.Config[StateID] & (uint)HudElementStates.IsVisible) > 0)
                        {
                            Vector2 elementSize = element.UnpaddedSize + element.Padding;

                            if (newSize[alignAxis] != 0)
                                elementSize[alignAxis] = newSize[alignAxis];

                            if (newSize[offAxis] != 0)
                                elementSize[offAxis] = newSize[offAxis];

                            element.UnpaddedSize = elementSize - element.Padding;
                            listSize[offAxis] = Math.Max(listSize[offAxis], elementSize[offAxis]);
                            listSize[alignAxis] += elementSize[alignAxis];
                            visCount++;
                        }
                    }

                    listSize[alignAxis] += Spacing * (visCount - 1);
                }

                return listSize;
            }


            public virtual Vector2 GetRangeSize(int start = 0, int end = -1)
            {
                Vector2 listSize = Vector2.Zero;
                int visCount = 0;

                if (hudCollectionList.Count > 0)
                {
                    if (end == -1)
                        end = hudCollectionList.Count - 1;

                    for (int i = start; i <= end; i++)
                    {
                        TElement element = hudCollectionList[i].Element;

                        if ((element.Config[StateID] & (uint)HudElementStates.IsVisible) > 0)
                        {
                            Vector2 elementSize = element.UnpaddedSize + element.Padding;
                            listSize[offAxis] = Math.Max(listSize[offAxis], elementSize[offAxis]);
                            listSize[alignAxis] += elementSize[alignAxis];
                            visCount++;
                        }
                    }

                    listSize[alignAxis] += Spacing * (visCount - 1);
                }

                return listSize;
            }


            protected virtual Vector2 GetBoundedRangeSize()
            {
                Vector2 minSize = MemberMinSize,
                    maxSize = MemberMaxSize,
                    listSize = Vector2.Zero;
                int visCount = 0;

                maxSize[offAxis] = (maxSize[offAxis] == 0f) ? UnpaddedSize[offAxis] : maxSize[offAxis];

                for (int i = 0; i < hudCollectionList.Count; i++)
                {
                    var entry = hudCollectionList[i];
                    TElement element = entry.Element;

                    if ((element.Config[StateID] & (uint)HudElementStates.IsVisible) > 0)
                    {
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
                bool isSelfSizing = (SizingMode & chainSelfSizingMask) > 0;
                bool isMemberSizeVariable = (SizingMode & memberVariableSizeMask) > 0;
                bool isSizeUninitialized = (UnpaddedSize.X == 0f || UnpaddedSize.Y == 0f);

                if (isSelfSizing || isSizeUninitialized)
                {
                    Vector2 chainBounds = UnpaddedSize;

                    if (isMemberSizeVariable)

                        rangeSize = GetBoundedRangeSize();
                    else

                        rangeSize = GetRangeSize();

                    if (rangeSize[alignAxis] > 0f)
                    {
                        if (chainBounds[alignAxis] == 0f || (SizingMode & HudChainSizingModes.FitChainAlignAxis) == HudChainSizingModes.FitChainAlignAxis)
                            chainBounds[alignAxis] = rangeSize[alignAxis];
                        else if ((SizingMode & HudChainSizingModes.ClampChainAlignAxis) == HudChainSizingModes.ClampChainAlignAxis)
                            chainBounds[alignAxis] = Math.Max(chainBounds[alignAxis], rangeSize[alignAxis]);
                    }

                    if (rangeSize[offAxis] > 0f)
                    {
                        if (chainBounds[offAxis] == 0f || (SizingMode & HudChainSizingModes.FitChainOffAxis) == HudChainSizingModes.FitChainOffAxis)
                            chainBounds[offAxis] = rangeSize[offAxis];
                        else if ((SizingMode & HudChainSizingModes.ClampChainOffAxis) == HudChainSizingModes.ClampChainOffAxis)
                            chainBounds[offAxis] = Math.Max(chainBounds[offAxis], rangeSize[offAxis]);
                    }

                    UnpaddedSize = chainBounds;
                }
            }


            protected override void Layout()
            {
                Vector2 chainBounds = UnpaddedSize;

                if (hudCollectionList.Count > 0 && (chainBounds.X > 0f && chainBounds.Y > 0f))
                {
                    UpdateRangeSize(chainBounds);

                    if (rangeLength > 0)
                    {
                        Vector2 startOffset = Vector2.Zero,
                            endOffset = Vector2.Zero;
                        float elementSpanLength = rangeSize[alignAxis];
                        float rcpSpanLength = 1f / Math.Max(elementSpanLength, 1E-6f);

                        elementSpanLength = Math.Min(elementSpanLength, chainBounds[alignAxis]);

                        if (alignAxis == 1)
                        {
                            if ((SizingMode & HudChainSizingModes.AlignMembersCenter) > 0)
                            {
                                startOffset.Y = .5f * elementSpanLength;
                                endOffset.Y = startOffset.Y - elementSpanLength;
                            }
                            else if ((SizingMode & HudChainSizingModes.AlignMembersEnd) > 0)
                            {
                                endOffset.Y = -.5f * chainBounds.Y;
                                startOffset.Y = endOffset.Y + elementSpanLength;
                            }
                            else
                            {
                                startOffset.Y = .5f * chainBounds.Y;
                                endOffset.Y = startOffset.Y - elementSpanLength;
                            }
                        }
                        else
                        {
                            if ((SizingMode & HudChainSizingModes.AlignMembersCenter) > 0)
                            {
                                startOffset.X = -.5f * elementSpanLength;
                                endOffset.X = startOffset.X + elementSpanLength;
                            }
                            else if ((SizingMode & HudChainSizingModes.AlignMembersEnd) > 0)
                            {
                                endOffset.X = .5f * chainBounds.X;
                                startOffset.X = endOffset.X - elementSpanLength;
                            }
                            else
                            {
                                startOffset.X = -.5f * chainBounds.X;
                                endOffset.X = startOffset.X + elementSpanLength;
                            }
                        }

                        UpdateMemberOffsets(startOffset, endOffset, rcpSpanLength);
                    }
                }

            }


            protected void UpdateRangeSize(Vector2 chainBounds)
            {
                rangeSize = Vector2.Zero;
                rangeLength = 0;

                int start = 0;
                int end = -1;

                float totalScale = 0f;
                float constantSpanLength = 0f;

                for (int i = 0; i < hudCollectionList.Count; i++)
                {
                    TElementContainer container = hudCollectionList[i];

                    if ((container.Element.Config[StateID] & (uint)HudElementStates.IsVisible) == 0)
                        continue;

                    if (end == -1) start = i;
                    end = i;

                    rangeLength++;
                    totalScale += container.AlignAxisScale;

                    if (container.AlignAxisScale == 0f)
                    {
                        Vector2 size = container.Element.UnpaddedSize + container.Element.Padding;
                        constantSpanLength += size[alignAxis];
                    }
                }

                if (rangeLength == 0) return;

                float totalSpacing = Spacing * (rangeLength - 1);

                bool reqPropScaling = (SizingMode & memberVariableAlignAxisMask) == 0;
                bool fitAlign = (SizingMode & HudChainSizingModes.FitMembersAlignAxis) > 0;
                bool clampAlign = (SizingMode & HudChainSizingModes.ClampMembersAlignAxis) > 0;
                bool fitOff = (SizingMode & HudChainSizingModes.FitMembersOffAxis) > 0;
                bool clampOff = (SizingMode & HudChainSizingModes.ClampMembersOffAxis) > 0;

                Vector2 minLimit = MemberMinSize;
                Vector2 maxLimit = MemberMaxSize;

                float propFixedSpace = 0f;
                float rcpTotalScale = 0f;

                if (reqPropScaling)
                {
                    propFixedSpace = Math.Max(chainBounds[alignAxis] - constantSpanLength - totalSpacing, 0f);
                    rcpTotalScale = Math.Min(1f / Math.Max(totalScale, 1f), 1f);
                }
                else
                {
                    float maxAllowedAlign = (chainBounds[alignAxis] - totalSpacing) / rangeLength;

                    if (maxAllowedAlign > 0f && (SizingMode & memberVariableAlignAxisMask) > 0)
                    {
                        maxLimit[alignAxis] = (maxLimit[alignAxis] == 0f || maxAllowedAlign < maxLimit[alignAxis])
                            ? maxAllowedAlign
                            : maxLimit[alignAxis];
                    }
                }

                if (chainBounds[offAxis] > 0f && (SizingMode & memberVariableOffAxisMask) > 0)
                {
                    maxLimit[offAxis] = (maxLimit[offAxis] == 0f || chainBounds[offAxis] < maxLimit[offAxis])
                        ? chainBounds[offAxis]
                        : maxLimit[offAxis];
                }

                for (int i = start; i <= end; i++)
                {
                    TElementContainer container = hudCollectionList[i];
                    TElement element = container.Element;

                    if ((element.Config[StateID] & (uint)HudElementStates.IsVisible) == 0)
                        continue;

                    Vector2 size = element.UnpaddedSize + element.Padding;

                    if (reqPropScaling)
                    {
                        if (container.AlignAxisScale != 0f && propFixedSpace > 0f)
                            size[alignAxis] = propFixedSpace * (container.AlignAxisScale * rcpTotalScale);
                    }
                    else
                    {
                        if (fitAlign)
                            size[alignAxis] = maxLimit[alignAxis];

                        else if (clampAlign)
                        {
                            if (maxLimit[alignAxis] > 0f)
                                size[alignAxis] = MathHelper.Clamp(size[alignAxis], minLimit[alignAxis], maxLimit[alignAxis]);
                            else
                                size[alignAxis] = Math.Max(size[alignAxis], minLimit[alignAxis]);
                        }
                    }

                    if (fitOff)
                        size[offAxis] = maxLimit[offAxis];

                    else if (clampOff)
                    {
                        if (maxLimit[offAxis] > 0f)
                            size[offAxis] = MathHelper.Clamp(size[offAxis], minLimit[offAxis], maxLimit[offAxis]);
                        else
                            size[offAxis] = Math.Max(size[offAxis], minLimit[offAxis]);
                    }

                    element.UnpaddedSize = size - element.Padding;

                    rangeSize[alignAxis] += size[alignAxis];
                    rangeSize[offAxis] = Math.Max(size[offAxis], rangeSize[offAxis]);
                }

                rangeSize[alignAxis] += totalSpacing;
            }


            protected void UpdateMemberOffsets(Vector2 startOffset, Vector2 endOffset, float rcpSpanLength, float offAxisOffset = 0f)
            {
                ParentAlignments left = (ParentAlignments)((int)ParentAlignments.Left * (2 - alignAxis)),
                    right = (ParentAlignments)((int)ParentAlignments.Right * (2 - alignAxis)),
                    bitmask = left | right;
                float j = 0f, spacingInc = Spacing * rcpSpanLength;

                for (int i = 0; i < hudCollectionList.Count; i++)
                {
                    TElementContainer container = hudCollectionList[i];
                    TElement element = container.Element;

                    if ((element.Config[StateID] & (uint)HudElementStates.IsVisible) > 0)
                    {
                        Vector2 size = element.UnpaddedSize + element.Padding;

                        element.ParentAlignment &= bitmask;
                        element.ParentAlignment |= ParentAlignments.Inner | ParentAlignments.UsePadding;

                        float increment = size[alignAxis] * rcpSpanLength;
                        Vector2 offset = Vector2.Lerp(startOffset, endOffset, j + (.5f * increment));

                        if ((element.ParentAlignment & left) == left)
                            offset[offAxis] += offAxisOffset;
                        else if ((element.ParentAlignment & right) == right)
                            offset[offAxis] -= offAxisOffset;

                        element.Offset = offset;
                        j += increment + spacingInc;
                    }
                }
            }
        }

        public class HudChain<TElementContainer> : HudChain<TElementContainer, HudElementBase>

            where TElementContainer : IChainElementContainer<HudElementBase>, new()
        {

            public HudChain(bool alignVertical = false, HudParentBase parent = null) : base(alignVertical, parent)
            { }


            public HudChain(HudParentBase parent) : base(true, parent)
            { }
        }

        public class HudChain : HudChain<HudElementContainer<HudElementBase>, HudElementBase>
        {

            public HudChain(bool alignVertical = false, HudParentBase parent = null) : base(alignVertical, parent)
            { }


            public HudChain(HudParentBase parent) : base(true, parent)
            { }
        }
    }
}