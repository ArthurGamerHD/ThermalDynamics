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


                void SetText(RichText text);


                void SetText(StringBuilder text, GlyphFormat? format = null);


                void SetText(string text, GlyphFormat? format = null);


                void Append(RichText text);


                void Append(StringBuilder text, GlyphFormat? format = null);


                void Append(string text, GlyphFormat? format = null);


                void Append(char ch, GlyphFormat? format = null);


                void Insert(RichText text, Vector2I start);


                void Insert(StringBuilder text, Vector2I start, GlyphFormat? format = null);


                void Insert(string text, Vector2I start, GlyphFormat? format = null);


                void Insert(char text, Vector2I start, GlyphFormat? format = null);


                void SetFormatting(GlyphFormat format);


                void SetFormatting(Vector2I start, Vector2I end, GlyphFormat format);


                RichText GetText();


                RichText GetTextRange(Vector2I start, Vector2I end);


                void RemoveAt(Vector2I index);


                void RemoveRange(Vector2I start, Vector2I end);


                void Clear();
            }
        }
    }
}