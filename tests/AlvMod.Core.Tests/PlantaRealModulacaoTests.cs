using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Planta;
using Xunit;
using Xunit.Abstractions;

namespace AlvMod.Core.Tests
{
    /// <summary>Modulação do pavimento inteiro da planta real (só roda com DadosLocais/limpa.json).</summary>
    public class PlantaRealModulacaoTests
    {
        private readonly ITestOutputHelper _saida;
        public PlantaRealModulacaoTests(ITestOutputHelper saida) => _saida = saida;

        [Fact]
        public void Modula_1a_e_2a_fiada_do_pavimento_sem_junta_a_prumo_e_sem_sobreposicao()
        {
            var planta = PlantaRealTests.DetectarReal();
            if (planta == null) return;
            Numeracao.Numerar(planta);

            var modulador = new Modulador(CatalogoPadrao.Familia39Concreto());
            var op = new OpcoesFiadas();
            var avisos = new List<string>();

            var f1 = PlanejadorFiadas.Trechos(planta, 1, op, avisos)
                .Select(t => (trecho: t, resultado: modulador.PrimeiraFiada(t.Comprimento, t.Bordas, t.Encontros))).ToList();
            var f2 = PlanejadorFiadas.Trechos(planta, 2, op, avisos)
                .Select(t =>
                {
                    var arestas = PlanejadorFiadas.Arestas(f1, t.Parede).Select(a => a - t.Inicio).ToList();
                    return (trecho: t, resultado: modulador.SegundaFiada(t.Comprimento, arestas, t.Bordas, t.Encontros));
                }).ToList();

            foreach (var (nome, fiada) in new[] { ("1ª", f1), ("2ª", f2) })
            {
                int ok = fiada.Count(x => x.resultado.Sucesso), parcial = fiada.Count(x => x.resultado.Parcial);
                _saida.WriteLine($"{nome} fiada: {fiada.Count} trechos, {ok} fecham, {parcial} parciais, " +
                                 $"{fiada.Count - ok - parcial} sem solução; peças: " +
                                 string.Join(", ", fiada.SelectMany(x => x.resultado.Pecas).GroupBy(p => p.Peca.Codigo)
                                     .OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}")));
            }
            foreach (var a in avisos.Distinct()) _saida.WriteLine("AVISO " + a);
            foreach (var (t, r) in f1.Where(x => !x.resultado.Sucesso).Take(15))
                _saida.WriteLine($"  1ª {t}: {r.Mensagem}");
            foreach (var (t, r) in f2.Where(x => !x.resultado.Sucesso && !x.resultado.Parcial).Take(20))
                _saida.WriteLine($"  2ª {t} bordas={t.Bordas}: {r.Mensagem} | arestas1: " +
                    string.Join(" ", PlanejadorFiadas.Arestas(f1, t.Parede).Where(a => a >= t.Inicio - 1 && a <= t.Fim + 1).Select(a => (a - t.Inicio).ToString("0.#"))));

            // Nenhuma peça de paredes diferentes se sobrepõe numa mesma fiada (encontros resolvidos).
            foreach (var fiada in new[] { f1, f2 })
            {
                var retangulos = fiada.SelectMany(x => x.resultado.Pecas.Select(p => Retangulo(x.trecho, p))).ToList();
                for (int i = 0; i < retangulos.Count; i++)
                    for (int j = i + 1; j < retangulos.Count; j++)
                    {
                        var a = retangulos[i]; var b = retangulos[j];
                        double ix = Math.Min(a.x1, b.x1) - Math.Max(a.x0, b.x0);
                        double iy = Math.Min(a.y1, b.y1) - Math.Max(a.y0, b.y0);
                        Assert.False(ix > 0.5 && iy > 0.5, $"peças sobrepostas em ({a.x0:0.#},{a.y0:0.#})");
                    }
            }

            // 2ª fiada: nenhuma junta a menos de 10 cm de uma aresta da 1ª na mesma parede.
            foreach (var (t, r) in f2.Where(x => x.resultado.Pecas.Count > 1))
            {
                var arestas1 = PlanejadorFiadas.Arestas(f1, t.Parede);
                foreach (var j in r.Juntas(1).Select(j => j + t.Inicio))
                    Assert.True(arestas1.All(a => Math.Abs(a - j) >= 9.99), $"{t.Nome}: junta a prumo em {j:0.#}");
            }

            var svg = Environment.GetEnvironmentVariable("ALVMOD_SVG");
            if (!string.IsNullOrEmpty(svg))
            {
                var segs = PlantaRealTests.Carregar();
                foreach (var (sufixo, fiada) in new[] { ("-f1", f1), ("-f2", f2) })
                    Svg.Gravar(svg.Replace(".svg", sufixo + ".svg"), segs, planta, Linhas(fiada));
            }
        }

        private static (double x0, double y0, double x1, double y1) Retangulo(TrechoFiada t, PecaPosicionada p)
        {
            var w = t.Parede;
            double a = t.Inicio + p.Inicio, b = t.Inicio + p.Fim, m = p.Peca.Largura / 2.0;
            return w.Horizontal ? (a, w.Coord - m, b, w.Coord + m) : (w.Coord - m, a, w.Coord + m, b);
        }

        private static IEnumerable<(double, double, double, double, string)> Linhas(
            List<(TrechoFiada trecho, ResultadoModulacao resultado)> fiada)
        {
            foreach (var (t, r) in fiada)
                foreach (var p in r.Pecas)
                {
                    var (x0, y0, x1, y1) = Retangulo(t, p);
                    string cor = p.Peca.Codigo switch { "39" => "#2a2", "19" => "#a2a", "34" => "#22f", "54" => "#f80", _ => "#f00" };
                    // Retângulo como 4 linhas.
                    yield return (x0, y0, x1, y0, cor);
                    yield return (x1, y0, x1, y1, cor);
                    yield return (x1, y1, x0, y1, cor);
                    yield return (x0, y1, x0, y0, cor);
                }
        }
    }
}
