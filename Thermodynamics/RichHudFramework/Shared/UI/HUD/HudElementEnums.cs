using System;
using VRage;
using VRageMath;

namespace RichHudFramework
{
    namespace UI
    {
        [Flags]
        public enum DimAlignments : byte
        {
            None = 0x0,

            Width = 0x1,

            Height = 0x2,

            Both = Width | Height,

            Size = Width | Height,

            IgnorePadding = 0x4,

            UnpaddedWidth = Width | IgnorePadding,

            UnpaddedHeight = Height | IgnorePadding,

            UnpaddedSize = Size | IgnorePadding
        }

        [Flags]
        public enum ParentAlignments : byte
        {
            Center = 0x0,

            Left = 0x1,

			Bottom = 0x2,

			Right = 0x4,

			Top = 0x8,

			TopLeft = Top | Left,

            TopRight = Top | Right,

            BottomLeft = Bottom | Left,

            BottomRight = Bottom | Right,

            InnerH = 0x10,

            InnerV = 0x20,

            UsePadding = 0x40,

            Inner = InnerH | InnerV,

            InnerLeft = InnerH | Left,

            InnerTop = InnerV | Top,

            InnerRight = InnerH | Right,

            InnerTopLeft = Inner | Top | Left,

            InnerTopRight = Inner | Top | Right,

            InnerBottomLeft = Inner | Bottom | Left,

            InnerBottomRight = Inner | Bottom | Right,

            InnerBottom = InnerV | Bottom,

            PaddedInnerLeft = UsePadding | InnerH | Left,

            PaddedInnerTop = UsePadding | InnerV | Top,

            PaddedInnerRight = UsePadding | InnerH | Right,

            PaddedInnerBottom = UsePadding | InnerV | Bottom,

            PaddedInnerTopLeft = UsePadding | Inner | Top | Left,

            PaddedInnerTopRight = UsePadding | Inner | Top | Right,

            PaddedInnerBottomLeft = UsePadding | Inner | Bottom | Left,

            PaddedInnerBottomRight = UsePadding | Inner | Bottom | Right
        }
    }
}