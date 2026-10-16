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

        private static (PlantaDetectada planta, List<(TrechoFiada trecho, ResultadoModulacao resultado)> f1,
            List<(TrechoFiada trecho, ResultadoModulacao resultado)> f2) Modular()
        {
            var planta = PlantaRealTests.DetectarReal();
            if (planta == null) return (null, null, null);
            Numeracao.Numerar(planta);

            var modulador = new Modulador(CatalogoPadrao.Familia39Concreto());
            var op = new OpcoesFiadas();
            var f1 = PlanejadorFiadas.Trechos(planta, 1, op)
                .Select(t => (trecho: t, resultado: modulador.PrimeiraFiada(t.Comprimento, t.Pontas))).ToList();
            var f2 = PlanejadorFiadas.Trechos(planta, 2, op)
                .Select(t =>
                {
                    var arestas = PlanejadorFiadas.Arestas(f1, t.Parede).Select(a => a - t.Inicio).ToList();
                    return (trecho: t, resultado: modulador.SegundaFiada(t.Comprimento, arestas, t.Pontas));
                }).ToList();
            return (planta, f1, f2);
        }

        [Fact]
        public void Portas_sao_lidas_pelo_tamanho_da_folha_mais_11()
        {
            var planta = PlantaRealTests.DetectarReal();
            if (planta == null) return;

            var portas = planta.Paredes.SelectMany(p => p.Vaos).Where(v => v.Tipo == TipoVao.Porta).ToList();
            _saida.WriteLine("Portas: " + string.Join(", ", portas.Select(v => $"abertura {v.Largura:0.#} / folha {v.Tamanho:0.#}")));

            Assert.All(portas, v => Assert.True(v.TamanhoLido, $"porta de {v.Largura:0.#} cm sem tamanho lido"));
            // A abertura desenhada inclui o marco (folha + 10): a abertura na alvenaria é folha + 11, centrada.
            foreach (var v in portas)
            {
                var (a, b) = v.Alvenaria(11);
                Assert.Equal(v.Tamanho + 11, b - a, 3);
                Assert.Equal(v.Centro, (a + b) / 2, 3);
                Assert.InRange(v.Largura - v.Tamanho, 5, 15);
            }
            Assert.Contains(portas, v => Math.Abs(v.Tamanho - 130) < 1);
            Assert.Contains(portas, v => Math.Abs(v.Tamanho - 150) < 1 || Math.Abs(v.Tamanho - 148.3) < 1);

            var janelas = planta.Paredes.SelectMany(p => p.Vaos).Where(v => v.Tipo == TipoVao.Janela).ToList();
            Assert.All(janelas, v => Assert.Equal(v.Largura, v.Tamanho, 1)); // vidro = abertura
            var (ja, jb) = janelas[0].Alvenaria(6);
            Assert.Equal(janelas[0].Tamanho + 6, jb - ja, 3);
        }

        [Fact]
        public void Regras_do_engenheiro_valem_em_todo_o_pavimento()
        {
            var (planta, f1, f2) = Modular();
            if (planta == null) return;

            foreach (var (nome, fiada) in new[] { ("1ª", f1), ("2ª", f2) })
            {
                int ok = fiada.Count(x => x.resultado.Sucesso), parcial = fiada.Count(x => x.resultado.Parcial);
                _saida.WriteLine($"{nome} fiada: {fiada.Count} trechos, {ok} fecham, {parcial} parciais, " +
                                 $"{fiada.Count - ok - parcial} sem solução; peças: " +
                                 string.Join(", ", fiada.SelectMany(x => x.resultado.Pecas).GroupBy(p => p.Peca.Codigo)
                                     .OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}")));
                var avisos = fiada.SelectMany(x => x.resultado.Avisos).GroupBy(a => a).Select(g => $"{g.Count()}× {g.Key}");
                _saida.WriteLine("  avisos: " + string.Join(" | ", avisos));
                foreach (var par in fiada.Where(x => !x.resultado.Sucesso).Take(12))
                    _saida.WriteLine($"  {par.trecho} {Descrever(par.trecho.Pontas)}: {par.resultado.Mensagem}");

                foreach (var par in fiada)
                {
                    var t = par.trecho;
                    var r = par.resultado;
                    var ps = r.Pecas;
                    for (int i = 0; i < ps.Count; i++)
                    {
                        bool ini = i == 0, ultimo = i == ps.Count - 1;
                        var peca = ps[i].Peca;
                        string onde = $"{nome} fiada {t.Nome} peça {i} ({peca.Codigo})";

                        // 3) 19 e 54 nunca no meio da parede.
                        if (peca.Funcao == FuncaoPeca.Meio || peca.Funcao == FuncaoPeca.Encontro)
                            Assert.True(ini || ultimo, $"{onde} no meio da parede");

                        // 1) 54 só na ponta de parede que atravessa um T.
                        if (peca.Funcao == FuncaoPeca.Encontro)
                            Assert.True((ini && t.Pontas.EncontroT.HasFlag(Bordas.Inicio)) || (ultimo && t.Pontas.EncontroT.HasFlag(Bordas.Fim)),
                                $"{onde} fora do encontro em T");

                        // 7) Bolacha nunca em ponta de porta, encontro ou canto.
                        if (peca.Funcao == FuncaoPeca.Compensador)
                        {
                            Assert.False(ini && t.Pontas.SemCompensador.HasFlag(Bordas.Inicio), $"{onde} bolacha na ponta inicial");
                            Assert.False(ultimo && t.Pontas.SemCompensador.HasFlag(Bordas.Fim), $"{onde} bolacha na ponta final");
                        }

                        // 4) Todas as peças a 1 cm uma da outra.
                        if (i > 0) Assert.Equal(1, ps[i].Inicio - ps[i - 1].Fim, 3);
                    }

                    // 2) Ponta que encosta em porta termina em 19 (quando não foi preciso relaxar).
                    if (ps.Count > 0 && !r.Avisos.Any(a => a.Contains("meio bloco")))
                    {
                        if (t.Pontas.Porta.HasFlag(Bordas.Inicio))
                            Assert.Equal(FuncaoPeca.Meio, ps.First().Peca.Funcao);
                        if (t.Pontas.Porta.HasFlag(Bordas.Fim))
                            Assert.Equal(FuncaoPeca.Meio, ps.Last().Peca.Funcao);
                    }

                    // 4) Quando sobra, a sobra vai para o lado da porta, nunca para o lado do encontro.
                    if (r.Parcial)
                    {
                        if (t.Pontas.Dura(Bordas.Fim) && !t.Pontas.Dura(Bordas.Inicio))
                            Assert.True(r.SobraNoInicio, $"{nome} fiada {t.Nome}: sobra do lado do encontro");
                        if (t.Pontas.Dura(Bordas.Inicio) && !t.Pontas.Dura(Bordas.Fim))
                            Assert.False(r.SobraNoInicio, $"{nome} fiada {t.Nome}: sobra do lado do encontro");
                    }
                }
            }
        }

        [Fact]
        public void Sem_sobreposicao_entre_paredes_e_2a_fiada_sem_prumo_exceto_avisos()
        {
            var (planta, f1, f2) = Modular();
            if (planta == null) return;

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

            foreach (var par in f2.Where(x => x.resultado.Pecas.Count > 1 &&
                                              !x.resultado.Avisos.Any(a => a.Contains("defasagem") || a.Contains("prumo"))))
            {
                var t = par.trecho;
                var arestas1 = PlanejadorFiadas.Arestas(f1, t.Parede);
                var juntas = par.resultado.Juntas(1).Select(j => j + t.Inicio).ToList();
                // Exceção combinada: a junta ao lado do 19 da porta é igual nas duas fiadas.
                if (t.Pontas.Porta.HasFlag(Bordas.Inicio) && juntas.Count > 0) juntas.RemoveAt(0);
                if (t.Pontas.Porta.HasFlag(Bordas.Fim) && juntas.Count > 0) juntas.RemoveAt(juntas.Count - 1);
                foreach (var j in juntas)
                    Assert.True(arestas1.All(a => Math.Abs(a - j) >= 9.99), $"{t.Nome}: junta a prumo em {j:0.#}");
            }

            var svg = Environment.GetEnvironmentVariable("ALVMOD_SVG");
            if (!string.IsNullOrEmpty(svg))
            {
                var segs = PlantaRealTests.Carregar();
                Svg.Gravar(svg.Replace(".svg", "-f1.svg"), segs, planta, Linhas(f1));
                Svg.Gravar(svg.Replace(".svg", "-f2.svg"), segs, planta, Linhas(f2));
            }
        }

        private static string Descrever(Pontas p) =>
            $"[semBolacha={p.SemCompensador} porta={p.Porta} T={p.EncontroT} canto={p.Canto}]";

        private static (double x0, double y0, double x1, double y1) Retangulo(TrechoFiada t, PecaPosicionada p)
        {
            var w = t.Parede;
            double a = t.Inicio + p.Inicio, b = t.Inicio + p.Fim, m = p.Peca.Largura / 2.0;
            return w.Horizontal ? (a, w.Coord - m, b, w.Coord + m) : (w.Coord - m, a, w.Coord + m, b);
        }

        private static IEnumerable<(double, double, double, double, string)> Linhas(
            List<(TrechoFiada trecho, ResultadoModulacao resultado)> fiada)
        {
            foreach (var par in fiada)
                foreach (var p in par.resultado.Pecas)
                {
                    var (x0, y0, x1, y1) = Retangulo(par.trecho, p);
                    string cor = p.Peca.Codigo switch { "39" => "#2a2", "19" => "#a2a", "34" => "#22f", "54" => "#f80", _ => "#f00" };
                    yield return (x0, y0, x1, y0, cor);
                    yield return (x1, y0, x1, y1, cor);
                    yield return (x1, y1, x0, y1, cor);
                    yield return (x0, y1, x0, y0, cor);
                }
        }
    }
}
