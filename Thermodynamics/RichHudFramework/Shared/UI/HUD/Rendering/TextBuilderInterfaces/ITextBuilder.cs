using VRageMath;
using System.Text;
using System;

namespace RichHudFramework
{
    namespace UI
    {
        public enum TextBuilderModes : int
        {
            Unlined = 1,

            Lined = 2,

            Wrapped = 3
        }

        namespace Rendering
        {
            public enum TextBuilderAccessors : int
            {
                LineWrapWidth = 1,

                BuilderMode = 2,

                GetRange = 3,

                SetFormatting = 4,

                RemoveRange = 5,

                Format = 6,

                ToString = 7,
            }

            public interface ITextBuilder : IIndexedCollection<ILine>
            {
				IRichChar this[Vector2I index] { get; }

                GlyphFormat Format { get; set; }

                float LineWrapWidth { get; set; }

				TextBuilderModes BuilderMode { get; set; }

/// <summary>Sets the text.</summary>
                void SetText(RichText text);

/// <summary>Sets the text.</summary>
                void SetText(StringBuilder text, GlyphFormat? format = null);

/// <summary>Sets the text.</summary>
                void SetText(string text, GlyphFormat? format = null);

/// <summary>Append operation.</summary>
                void Append(RichText text);

/// <summary>Append operation.</summary>
                void Append(StringBuilder text, GlyphFormat? format = null);

/// <summary>Append operation.</summary>
                void Append(string text, GlyphFormat? format = null);

/// <summary>Append operation.</summary>
                void Append(char ch, GlyphFormat? format = null);

/// <summary>Insert operation.</summary>
                void Insert(RichText text, Vector2I start);

/// <summary>Insert operation.</summary>
                void Insert(StringBuilder text, Vector2I start, GlyphFormat? format = null);

/// <summary>Insert operation.</summary>
                void Insert(string text, Vector2I start, GlyphFormat? format = null);

/// <summary>Insert operation.</summary>
                void Insert(char text, Vector2I start, GlyphFormat? format = null);

/// <summary>Sets the formatting.</summary>
                void SetFormatting(GlyphFormat format);

/// <summary>Sets the formatting.</summary>
                void SetFormatting(Vector2I start, Vector2I end, GlyphFormat format);

/// <summary>Returns the text.</summary>
                RichText GetText();

/// <summary>Returns the textrange.</summary>
                RichText GetTextRange(Vector2I start, Vector2I end);

/// <summary>Removes the at.</summary>
                void RemoveAt(Vector2I index);

/// <summary>Removes the range.</summary>
                void RemoveRange(Vector2I start, Vector2I end);

/// <summary>Clear operation.</summary>
                void Clear();
            }
        }
    }
}