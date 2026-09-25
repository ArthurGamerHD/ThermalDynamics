using System;
using VRageMath;
using GlyphFormatMembers = VRage.MyTuple<byte, float, VRageMath.Vector2I, VRageMath.Color>;

namespace RichHudFramework
{
    namespace UI
    {
        using Rendering.Client;
        using Rendering.Server;
        using Rendering;

        public enum TextAlignment : byte
        {
            Left = 0,
            Center = 1,
            Right = 2,
        }

		public struct GlyphFormat : IEquatable<GlyphFormat>
        {

            public static readonly GlyphFormat Black = new GlyphFormat();


            public static readonly GlyphFormat White = new GlyphFormat(color: Color.White);


            public static readonly GlyphFormat Blueish = new GlyphFormat(color: new Color(220, 235, 242));


			public static readonly GlyphFormat Empty = new GlyphFormat(default(GlyphFormatMembers));

			public TextAlignment Alignment => (TextAlignment)Data.Item1;

			public float TextSize => Data.Item2;

			public IFontMin Font => FontManager.GetFont(Data.Item3.X);

			public FontStyles FontStyle => (FontStyles)Data.Item3.Y;

			public Vector2I StyleIndex => Data.Item3;

			public Color Color => Data.Item4;

			public GlyphFormatMembers Data { get; set; }



			public GlyphFormat(Color color, TextAlignment alignment, float textSize,
				string fontName, FontStyles style = FontStyles.Regular) :
				this(color, alignment, textSize, style, FontManager.GetFont(fontName))
			{ }


			public GlyphFormat(Color color, TextAlignment alignment, float textSize, Vector2I fontStyle)
			{
				if (color == default(Color))
					color = Color.Black;


				Data = new GlyphFormatMembers((byte)alignment, textSize, fontStyle, color);
			}


			public GlyphFormat(Color color = default(Color), TextAlignment alignment = TextAlignment.Left,
				float textSize = 1f, FontStyles style = FontStyles.Regular, IFontMin font = null)
			{
				if (color == default(Color))
					color = Color.Black;
				if (font == null)
					font = FontManager.GetFont(FontManager.Default.X);


				Data = new GlyphFormatMembers((byte)alignment, textSize, font.GetStyleIndex(style), color);
			}


			public GlyphFormat(GlyphFormatMembers data) { this.Data = data; }


			public GlyphFormat(GlyphFormat original) { Data = original.Data; }



			public GlyphFormat WithColor(Color color) =>

				new GlyphFormat(color, Alignment, TextSize, StyleIndex);


			public GlyphFormat WithAlignment(TextAlignment textAlignment) =>

				new GlyphFormat(Color, textAlignment, TextSize, StyleIndex);


			public GlyphFormat WithFont(int font) =>

				new GlyphFormat(Color, Alignment, TextSize, new Vector2I(font, StyleIndex.Y));


			public GlyphFormat WithFont(IFontMin font, FontStyles style = FontStyles.Regular) =>

				new GlyphFormat(Color, Alignment, TextSize, style, font);


			public GlyphFormat WithFont(string fontName, FontStyles style = FontStyles.Regular) =>

				new GlyphFormat(Color, Alignment, TextSize, style, FontManager.GetFont(fontName));


			public GlyphFormat WithFont(Vector2I fontStyle) =>

				new GlyphFormat(Color, Alignment, TextSize, fontStyle);


			public GlyphFormat WithStyle(FontStyles style)
			{
				if (FontManager.GetFont(StyleIndex.X).IsStyleDefined(style))
					return new GlyphFormat(Color, Alignment, TextSize, new Vector2I(StyleIndex.X, (int)style));
				else
					return this;
			}


			public GlyphFormat WithStyle(int style)
			{
				if (FontManager.GetFont(StyleIndex.X).IsStyleDefined(style))
					return new GlyphFormat(Color, Alignment, TextSize, new Vector2I(StyleIndex.X, style));
				else
					return this;
			}


			public GlyphFormat WithSize(float size) =>

				new GlyphFormat(Color, Alignment, size, StyleIndex);



			public override bool Equals(object obj)
            {
                if (obj == null || !(obj is GlyphFormat))
                    return false;

                return Equals((GlyphFormat)obj);
            }


			public bool Equals(GlyphFormat format)
            {
                return Data.Item1 == format.Data.Item1
                    && Data.Item2 == format.Data.Item2
                    && Data.Item3 == format.Data.Item3
                    && Data.Item4 == format.Data.Item4;
            }


            public bool DataEqual(GlyphFormatMembers data)
            {
                return Data.Item1 == data.Item1
                    && Data.Item2 == data.Item2
                    && Data.Item3 == data.Item3
                    && Data.Item4 == data.Item4;
            }


			public override int GetHashCode() =>
                Data.GetHashCode();
        }
    }
}