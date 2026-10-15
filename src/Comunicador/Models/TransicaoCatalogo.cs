using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace Comunicador.Models;

public sealed record OpcaoTransicao(string Id, string Nome);

public sealed record CategoriaTransicao(string Nome, IReadOnlyList<OpcaoTransicao> Opcoes);

/// <summary>Galeria de transições organizada conforme as famílias solicitadas.</summary>
public static class TransicaoCatalogo
{
    public static IReadOnlyList<CategoriaTransicao> Categorias { get; } =
    [
        Criar("Opacidade e transparência", "Sem efeito|Fade In|Fade Out|Crossfade|Fade Through Black|Fade Through White|Fade Through Color|Dissolve|Film Burn|Light Leak|Flash"),
        Criar("Deslizamento", "Slide Left|Slide Right|Slide Up|Slide Down|Push Left|Push Right|Push Up|Push Down|Slide Over|Slide Under|Split Slide|Diagonal Slide|Parallax Slide|Stack Transition|Swipe Transition"),
        Criar("Zoom e escala", "Zoom In|Zoom Out|Zoom Through|Zoom Through Tunnel|Zoom In From Center|Zoom Out To Center|Scale Up|Scale Down|Pop In|Pop Out|Expand|Shrink|Pulse Transition|Overshoot Zoom|Camera Pull"),
        Criar("Rotação e efeitos 3D", "Rotate In|Rotate Out|Spin|Flip Horizontal|Flip Vertical|3D Flip|Flip Card|Page Turn|Page Curl|Cube Rotation|Carousel Rotation|Barrel Roll|Tilt Transition|Perspective Zoom|Fold Transition|Unfold Transition|Swing Transition|Hinge Transition"),
        Criar("Máscara e revelação", "Circle Reveal|Wipe / Diagonal Wipe|Curtain Open|Curtain Close|Wipe Left|Wipe Right|Wipe Up|Wipe Down|Wipe Diagonal|Circular Wipe|Iris In|Iris Out|Box Reveal|Polygon Reveal|Star Reveal|Diamond Reveal|Heart Reveal|Cross Reveal|Radial Wipe|Clock Wipe|Blinds|Venetian Blinds|Shutter|Split Reveal|Center Split|Edge Reveal|Mask Expansion|Mask Collapse"),
        Criar("Desfoque e foco", "Blur In|Blur Out|Blur Crossfade|Focus Pull|Defocus|Gaussian Blur Transition|Motion Blur|Directional Blur|Radial Blur|Zoom Blur|Depth of Field|Sharpness Reveal"),
        Criar("Movimento físico", "Bounce In|Bounce Out|Elastic In|Elastic Out|Spring Transition|Rubber Band|Overshoot|Anticipation|Recoil|Inertia|Momentum Slide|Drop In|Fall Out|Fly In|Fly Out|Throw Transition|Gravity Drop|Swing In|Wobble|Jello|Rubber Stretch"),
        Criar("Formas e deformação", "Morph|Shape Morph|Circle Morph|Blob Morph|Liquid Transition|Gooey Transition|Wave Transition|Ripple|Warp|Swirl|Vortex|Twist|Squeeze|Stretch|Skew|Melt|Shatter|Break Apart|Assemble|Particle Dissolve|Liquid Wipe|Ink Spread|Paint Reveal"),
        Criar("Digital e futurista", "Glitch|RGB Split|Chromatic Aberration|Pixelate|Pixel Dissolve|Digital Noise|Static Transition|Scanline Reveal|Data Stream|Matrix Rain|Grid Reveal|Grid Dissolve|Mosaic Transition|Hexagon Reveal|Circuit Transition|Hologram Transition|RGB Glitch|VHS Transition|Digital Scramble|Pixel Sorting|Scan Transition|Digital Shatter"),
        Criar("Luz, cor e ambiente", "Color Fade|Color Wipe|Gradient Wipe|Gradient Shift|Exposure Flash|White Flash|Blackout|Glow Transition|Bloom Transition|Neon Wipe|Lens Flare|Light Sweep|Spotlight Reveal|Shadow Reveal|Burn Transition|Fire Transition|Smoke Transition|Fog Transition|Frost Transition|Ice Shatter|Electric Arc|Aurora Transition"),
        Criar("Cinema e câmera", "Whip Pan|Whip Zoom|Match Cut|Match Move|Dolly In|Dolly Out|Truck Left|Truck Right|Pedestal Up|Pedestal Down|Orbit Transition|Tracking Transition|Handheld Shake|Dutch Tilt|Spin Camera|Crash Zoom|Pull Focus|Zoom Match|Luma Fade|Luma Wipe|Film Grain Dissolve|Film Reel Transition|Cinematic Letterbox"),
        Criar("Elementos e interface", "Shared Element Transition|Hero Transition|Card Expand|Card Collapse|Modal Fade|Modal Slide|Modal Zoom|Drawer Slide|Accordion Expand|Accordion Collapse|Tab Crossfade|List Stagger|Staggered Reveal|Text Reveal|Typewriter|Word-by-Word|Letter-by-Letter|Split Text|Icon Morph|Layout Morph|Expand From Click|Shared Axis|Portal Transition"),
        Criar("Padrões geométricos", "Checkerboard|Grid Wipe|Squares Reveal|Tiles Dissolve|Stripes Wipe|Horizontal Bars|Vertical Bars|Diagonal Stripes|Triangle Wipe|Hexagonal Grid|Honeycomb Reveal|Spiral Wipe|Radial Sectors|Concentric Circles|Mosaic Scatter|Tile Flip|Geometric Shatter|Origami Fold"),
        Criar("Especiais e combinações", "Fade + Slide|Fade + Zoom|Fade + Blur|Slide + Scale|Zoom + Blur|Rotate + Fade|Flip + Zoom|Wipe + Glow|Glitch + RGB Split|Morph + Liquid|Parallax + Fade|Elastic Slide|3D Zoom + Rotation|Light Sweep + Crossfade|Portal + Zoom|Shatter + Reassemble|Blur + Slide + Fade"),
    ];

    private static CategoriaTransicao Criar(string nome, string opcoes)
    {
        var itens = opcoes.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(titulo => new OpcaoTransicao(CriarId(titulo), titulo))
            .ToArray();
        return new CategoriaTransicao(nome, itens);
    }

    private static string CriarId(string titulo)
    {
        if (titulo == "Sem efeito") return "none";
        var semAcentos = titulo.Normalize(NormalizationForm.FormD);
        var semMarcas = new string(semAcentos.Where(c => CharUnicodeInfo.GetUnicodeCategory(c)
            != UnicodeCategory.NonSpacingMark).ToArray());
        return Regex.Replace(semMarcas.ToLowerInvariant(), "[^a-z0-9]+", "_").Trim('_');
    }
}
