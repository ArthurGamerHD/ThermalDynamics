using System;
using VRageMath;

namespace RichHudFramework
{
    namespace UI
    {
        namespace Rendering
        {
            public enum TextBoardAccessors : int
            {
                AutoResize = 129,

                VertAlign = 130,

                MoveToChar = 131,

                GetCharAtOffset = 132,

                OnTextChanged = 133,

                TextOffset = 134,

                VisibleLineRange = 135,
            }

            public interface ITextBoard : ITextBuilder
            {
                event Action TextChanged;

                float Scale { get; set; }

                Vector2 Size { get; }

                Vector2 TextSize { get; }

                Vector2 TextOffset { get; set; }

                Vector2I VisibleLineRange { get; }

                Vector2 FixedSize { get; set; }

                bool AutoResize { get; set; }

                bool VertCenterText { get; set; }

/// <summary>MoveToChar operation.</summary>
				void MoveToChar(Vector2I index);

/// <summary>Returns the charatoffset.</summary>
                Vector2I GetCharAtOffset(Vector2 localPos);

/// <summary>Draw operation.</summary>
                void Draw(BoundingBox2 box, BoundingBox2 mask, MatrixD[] matrix);
            }
        }
    }
}