using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using AlvMod.Core.Planta;
using Xunit;
using Xunit.Abstractions;

namespace AlvMod.Core.Tests
{
    /// <summary>
    /// Testes contra a planta real do engenheiro (DadosLocais/limpa.json, exportada do DWG em cm).
    /// O arquivo NÃO vai para o repositório público: sem ele, os testes passam sem fazer nada.
    /// Com ALVMOD_SVG=caminho, grava a detecção em SVG para conferência visual.
    /// </summary>
    public class PlantaRealTests
    {
        private readonly ITestOutputHelper _saida;
        public PlantaRealTests(ITestOutputHelper saida) => _saida = saida;

        internal static string Caminho =>
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "DadosLocais", "limpa.json");

        internal static List<Segmento> Carregar() => CarregarTudo().segs;

        internal static (List<Segmento> segs, List<Rotulo> rotulos) CarregarTudo()
        {
            if (!File.Exists(Caminho)) return (null, null);
            using var doc = JsonDocument.Parse(File.ReadAllText(Caminho));
            var segs = doc.RootElement.GetProperty("segs").EnumerateArray().Select(e => new Segmento(
                e.GetProperty("l").GetString(),
                e.GetProperty("x1").GetDouble(), e.GetProperty("y1").GetDouble(),
                e.GetProperty("x2").GetDouble(), e.GetProperty("y2").GetDouble())).ToList();
            var rotulos = doc.RootElement.GetProperty("textos").EnumerateArray().Select(e => new Rotulo(
                e.GetProperty("t").GetString(), e.GetProperty("x").GetDouble(), e.GetProperty("y").GetDouble())).ToList();
            return (segs, rotulos);
        }

        internal static PlantaDetectada DetectarReal()
        {
            var (segs, rotulos) = CarregarTudo();
            return segs == null ? null : DetectorParedes.Detectar(segs, Opcoes(), rotulos);
        }

        internal static OpcoesDeteccao Opcoes() => new OpcoesDeteccao
        {
            LayersParede = { "Alvenaria" },
            LayersPorta = { "Portas" },
            LayersJanela = { "Arq_Peitoril", "Arq_Esq_Vidro" }
        };

        [Fact]
        public void Detecta_paredes_encontros_e_vaos_da_planta_real()
        {
            var segs = Carregar();
            if (segs == null) return;

            var planta = DetectarReal();

            var vaos = planta.Paredes.SelectMany(p => p.Vaos).ToList();
            _saida.WriteLine($"Paredes: {planta.Paredes.Count} (H {planta.Paredes.Count(p => p.Horizontal)}, V {planta.Paredes.Count(p => !p.Horizontal)})");
            _saida.WriteLine("Espessuras: " + string.Join(", ", planta.Paredes.GroupBy(p => Math.Round(p.Espessura)).Select(g => $"{g.Key}cm×{g.Count()}")));
            _saida.WriteLine("Encontros: " + string.Join(", ", planta.Encontros.GroupBy(e => e.Tipo).Select(g => $"{g.Key}={g.Count()}")));
            _saida.WriteLine("Vãos: " + string.Join(", ", vaos.GroupBy(v => v.Tipo).Select(g => $"{g.Key}={g.Count()}")));
            foreach (var a in planta.Avisos) _saida.WriteLine("AVISO " + a);
            foreach (var p in planta.Paredes.OrderBy(p => p.Orientacao).ThenByDescending(p => p.Coord).ThenBy(p => p.Inicio))
                _saida.WriteLine("  " + p + (p.Vaos.Count > 0 ? "  vãos: " + string.Join(" ", p.Vaos.Select(v => $"{v.Tipo}[{v.Inicio:0.#};{v.Fim:0.#}]")) : ""));

            var svg = Environment.GetEnvironmentVariable("ALVMOD_SVG");
            if (!string.IsNullOrEmpty(svg)) Svg.Gravar(svg, segs, planta, null);

            Assert.True(planta.Paredes.Count >= 40);
            Assert.Contains(planta.Encontros, e => e.Tipo == TipoEncontro.L);
            Assert.Contains(planta.Encontros, e => e.Tipo == TipoEncontro.T);
            Assert.Contains(vaos, v => v.Tipo == TipoVao.Porta);
            Assert.Contains(vaos, v => v.Tipo == TipoVao.Janela);
        }
    }

    /// <summary>Desenho SVG simples da detecção (só para conferência nos testes).</summary>
    internal static class Svg
    {
        public static void Gravar(string caminho, List<Segmento> segs, PlantaDetectada planta,
            IEnumerable<(double x1, double y1, double x2, double y2, string cor)> extras)
        {
            var ci = CultureInfo.InvariantCulture;
            double x0 = segs.Min(s => Math.Min(s.X1, s.X2)) - 50, x1 = segs.Max(s => Math.Max(s.X1, s.X2)) + 50;
            double y0 = segs.Min(s => Math.Min(s.Y1, s.Y2)) - 50, y1 = segs.Max(s => Math.Max(s.Y1, s.Y2)) + 50;
            double esc = 2400 / (x1 - x0);
            string X(double x) => ((x - x0) * esc).ToString("0.0", ci);
            string Y(double y) => ((y1 - y) * esc).ToString("0.0", ci);
            var sb = new StringBuilder();
            sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{((x1 - x0) * esc).ToString("0", ci)}\" height=\"{((y1 - y0) * esc).ToString("0", ci)}\" style=\"background:#fff\">");
            foreach (var s in segs)
            {
                string cor = s.Layer == "Alvenaria" ? "#bbb" : s.Layer == "Portas" ? "#f9b" : s.Layer.StartsWith("Arq_") ? "#9cf" : null;
                if (cor == null) continue;
                sb.Append($"<line x1=\"{X(s.X1)}\" y1=\"{Y(s.Y1)}\" x2=\"{X(s.X2)}\" y2=\"{Y(s.Y2)}\" stroke=\"{cor}\" stroke-width=\"1\"/>");
            }
            foreach (var p in planta.Paredes)
            {
                var (ax, ay) = p.Ponto(p.Inicio);
                var (bx, by) = p.Ponto(p.Fim);
                sb.Append($"<line x1=\"{X(ax)}\" y1=\"{Y(ay)}\" x2=\"{X(bx)}\" y2=\"{Y(by)}\" stroke=\"#000\" stroke-width=\"1.5\"/>");
                foreach (var v in p.Vaos)
                {
                    var (vx1, vy1) = p.Ponto(v.Inicio);
                    var (vx2, vy2) = p.Ponto(v.Fim);
                    string cor = v.Tipo == TipoVao.Porta ? "#e00" : v.Tipo == TipoVao.Janela ? "#08f" : "#f80";
                    sb.Append($"<line x1=\"{X(vx1)}\" y1=\"{Y(vy1)}\" x2=\"{X(vx2)}\" y2=\"{Y(vy2)}\" stroke=\"{cor}\" stroke-width=\"5\"/>");
                }
                if (p.Nome != null)
                {
                    var (mx, my) = p.Ponto((p.Inicio + p.Fim) / 2);
                    sb.Append($"<text x=\"{X(mx)}\" y=\"{Y(my)}\" font-size=\"11\" fill=\"#060\" font-family=\"Arial\">{p.Nome.Replace("PAREDE ", "")}</text>");
                }
            }
            if (extras != null)
                foreach (var (ex1, ey1, ex2, ey2, cor) in extras)
                    sb.Append($"<line x1=\"{X(ex1)}\" y1=\"{Y(ey1)}\" x2=\"{X(ex2)}\" y2=\"{Y(ey2)}\" stroke=\"{cor}\" stroke-width=\"3\"/>");
            foreach (var e in planta.Encontros)
            {
                string cor = e.Tipo == TipoEncontro.L ? "#0a0" : e.Tipo == TipoEncontro.T ? "#a0a" : e.Tipo == TipoEncontro.X ? "#00a" : "#888";
                sb.Append($"<circle cx=\"{X(e.X)}\" cy=\"{Y(e.Y)}\" r=\"4\" fill=\"{cor}\"/>");
            }
            sb.Append("</svg>");
            File.WriteAllText(caminho, sb.ToString());
        }
    }
}
