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

        private sealed class Resultado
        {
            public PlantaDetectada Planta;
            public PlanoFiada P1, P2;
            public List<(TrechoFiada trecho, ResultadoModulacao resultado)> F1, F2;
        }

        private static Resultado Modular()
        {
            var planta = PlantaRealTests.DetectarReal();
            if (planta == null) return null;
            Numeracao.Numerar(planta);

            var familia = CatalogoPadrao.Familia39Concreto();
            var modulador = new Modulador(familia);
            var op = new OpcoesFiadas { BlocoEncontro = familia.Buscar("54") };
            var res = new Resultado { Planta = planta, P1 = PlanejadorFiadas.Planejar(planta, 1, op), P2 = PlanejadorFiadas.Planejar(planta, 2, op) };
            res.F1 = res.P1.Trechos.Select(t => (trecho: t, resultado: modulador.PrimeiraFiada(t.Comprimento, t.Pontas))).ToList();
            res.F2 = res.P2.Trechos.Select(t =>
            {
                var arestas = PlanejadorFiadas.Arestas(res.F1, t.Parede, 1, res.P1.Fixas).Select(a => a - t.Inicio).ToList();
                return (trecho: t, resultado: modulador.SegundaFiada(t.Comprimento, arestas, t.Pontas));
            }).ToList();
            return res;
        }

        [Fact]
        public void Portas_sao_lidas_pelo_tamanho_da_folha_mais_11()
        {
            var planta = PlantaRealTests.DetectarReal();
            if (planta == null) return;

            var portas = planta.Paredes.SelectMany(p => p.Vaos).Where(v => v.Tipo == TipoVao.Porta).ToList();
            Assert.All(portas, v => Assert.True(v.TamanhoLido, $"porta de {v.Largura:0.#} cm sem tamanho lido"));
            foreach (var v in portas)
            {
                var (a, b) = v.Alvenaria(11);
                Assert.Equal(v.Tamanho + 11, b - a, 3);
                Assert.Equal(v.Centro, (a + b) / 2, 3);
                Assert.InRange(v.Largura - v.Tamanho, 5, 15);
            }
            var janelas = planta.Paredes.SelectMany(p => p.Vaos).Where(v => v.Tipo == TipoVao.Janela).ToList();
            Assert.All(janelas, v => Assert.Equal(v.Largura, v.Tamanho, 1)); // vidro = abertura
        }

        [Fact]
        public void Todo_T_e_toda_cruz_tem_bloco_54()
        {
            var r = Modular();
            if (r == null) return;

            var fixas = r.P1.Fixas.Concat(r.P2.Fixas).ToList();
            foreach (var e in r.Planta.Encontros.Where(e => e.Tipo == TipoEncontro.T || e.Tipo == TipoEncontro.X))
            {
                bool tem = fixas.Any(f => e.Paredes.Contains(f.Parede) &&
                                          f.Parede.AoLongo(e.X, e.Y) > f.Inicio && f.Parede.AoLongo(e.X, e.Y) < f.Fim &&
                                          Math.Abs(f.Parede.Transversal(e.X, e.Y) - f.Parede.Coord) < 2);
                Assert.True(tem, $"encontro {e} sem 54");
            }
            _saida.WriteLine($"54 fixos: 1ª fiada {r.P1.Fixas.Count}, 2ª fiada {r.P2.Fixas.Count}; " +
                             $"T {r.Planta.Encontros.Count(e => e.Tipo == TipoEncontro.T)}, X {r.Planta.Encontros.Count(e => e.Tipo == TipoEncontro.X)}");
        }

        [Fact]
        public void Regras_do_engenheiro_valem_em_todo_o_pavimento()
        {
            var r = Modular();
            if (r == null) return;

            foreach (var (nome, fiada) in new[] { ("1ª", r.F1), ("2ª", r.F2) })
            {
                int ok = fiada.Count(x => x.resultado.Sucesso), parcial = fiada.Count(x => x.resultado.Parcial);
                _saida.WriteLine($"{nome} fiada: {fiada.Count} trechos, {ok} fecham, {parcial} parciais, " +
                                 $"{fiada.Count - ok - parcial} sem solução; peças: " +
                                 string.Join(", ", fiada.SelectMany(x => x.resultado.Pecas).GroupBy(p => p.Peca.Codigo)
                                     .OrderBy(g => g.Key).Select(g => $"{g.Key}×{g.Count()}")));
                var avisos = fiada.SelectMany(x => x.resultado.Avisos).GroupBy(a => a).Select(g => $"{g.Count()}× {g.Key}");
                _saida.WriteLine("  avisos: " + string.Join(" | ", avisos));
                foreach (var par in fiada.Where(x => x.resultado.Pecas.Count == 0))
                    _saida.WriteLine($"  sem solução: {par.trecho} — {par.resultado.Mensagem}");

                foreach (var par in fiada)
                {
                    var t = par.trecho;
                    var res = par.resultado;
                    var ps = res.Pecas;
                    for (int i = 0; i < ps.Count; i++)
                    {
                        bool ini = i == 0, ultimo = i == ps.Count - 1;
                        var peca = ps[i].Peca;
                        string onde = $"{nome} fiada {t.Nome} peça {i} ({peca.Codigo})";

                        // 54 só como peça fixa nos encontros.
                        Assert.NotEqual(FuncaoPeca.Encontro, peca.Funcao);

                        // 19 só em ponta sem continuidade: nunca no meio, nunca em encontro.
                        if (peca.Funcao == FuncaoPeca.Meio)
                        {
                            Assert.True(ini || ultimo, $"{onde} no meio da parede");
                            Assert.False(ini && t.Pontas.Encontro.HasFlag(Bordas.Inicio), $"{onde} em encontro");
                            Assert.False(ultimo && t.Pontas.Encontro.HasFlag(Bordas.Fim), $"{onde} em encontro");
                        }

                        // Bolacha nunca em ponta de porta, encontro ou canto.
                        if (peca.Funcao == FuncaoPeca.Compensador)
                        {
                            Assert.False(ini && t.Pontas.SemCompensador.HasFlag(Bordas.Inicio), $"{onde} bolacha na ponta inicial");
                            Assert.False(ultimo && t.Pontas.SemCompensador.HasFlag(Bordas.Fim), $"{onde} bolacha na ponta final");
                        }

                        // Juntas de 1 cm (a diferença de até 0,5 cm do desenho dividida entre as juntas).
                        if (i > 0) Assert.InRange(ps[i].Inicio - ps[i - 1].Fim, 0.83, 1.17);
                    }

                    // Peças do encontro (34 na que passa, 39 na que encosta) quando não houve aviso.
                    if (ps.Count > 0 && res.Avisos.Count == 0)
                    {
                        if (t.Pontas.Passa34.HasFlag(Bordas.Inicio) && !res.SobraNoInicio) Assert.Equal("34", ps.First().Peca.Codigo);
                        if (t.Pontas.Exige39.HasFlag(Bordas.Inicio) && !res.SobraNoInicio) Assert.Equal("39", ps.First().Peca.Codigo);
                        if (res.Sucesso && t.Pontas.Passa34.HasFlag(Bordas.Fim)) Assert.Equal("34", ps.Last().Peca.Codigo);
                        if (res.Sucesso && t.Pontas.Exige39.HasFlag(Bordas.Fim)) Assert.Equal("39", ps.Last().Peca.Codigo);
                    }

                    // Porta: termina em 19 (quando não foi preciso relaxar).
                    if (ps.Count > 0 && !res.Avisos.Any(a => a.Contains("meio bloco")))
                    {
                        if (t.Pontas.Porta.HasFlag(Bordas.Inicio)) Assert.Equal(FuncaoPeca.Meio, ps.First().Peca.Funcao);
                        if (t.Pontas.Porta.HasFlag(Bordas.Fim)) Assert.Equal(FuncaoPeca.Meio, ps.Last().Peca.Funcao);
                    }

                    // Quem encosta em outra parede encosta a 1 cm: a peça chega até a ponta do trecho.
                    if (res.Sucesso)
                    {
                        if (t.Pontas.Dura(Bordas.Inicio)) Assert.InRange(ps.First().Inicio, -0.2, 0.2);
                        if (t.Pontas.Dura(Bordas.Fim)) Assert.InRange(t.Comprimento - ps.Last().Fim, -0.2, 0.2);
                    }
                    if (res.Parcial)
                    {
                        if (t.Pontas.Dura(Bordas.Fim) && !t.Pontas.Dura(Bordas.Inicio))
                            Assert.True(res.SobraNoInicio, $"{nome} fiada {t.Nome}: sobra do lado do encontro");
                        if (t.Pontas.Dura(Bordas.Inicio) && !t.Pontas.Dura(Bordas.Fim))
                            Assert.False(res.SobraNoInicio, $"{nome} fiada {t.Nome}: sobra do lado do encontro");
                    }
                }
            }
        }

        [Fact]
        public void Sem_sobreposicao_e_2a_fiada_sem_prumo_exceto_avisos()
        {
            var r = Modular();
            if (r == null) return;

            foreach (var (fiada, plano) in new[] { (r.F1, r.P1), (r.F2, r.P2) })
            {
                var retangulos = fiada.SelectMany(x => x.resultado.Pecas.Select(p => Retangulo(x.trecho.Parede, x.trecho.Inicio + p.Inicio, x.trecho.Inicio + p.Fim, p.Peca.Largura)))
                    .Concat(plano.Fixas.Select(f => Retangulo(f.Parede, f.Inicio, f.Fim, f.Peca.Largura))).ToList();
                for (int i = 0; i < retangulos.Count; i++)
                    for (int j = i + 1; j < retangulos.Count; j++)
                    {
                        var a = retangulos[i]; var b = retangulos[j];
                        double ix = Math.Min(a.x1, b.x1) - Math.Max(a.x0, b.x0);
                        double iy = Math.Min(a.y1, b.y1) - Math.Max(a.y0, b.y0);
                        Assert.False(ix > 0.5 && iy > 0.5, $"peças sobrepostas em ({a.x0:0.#},{a.y0:0.#})");
                    }
            }

            int ruins = 0;
            foreach (var par in r.F2.Where(x => x.resultado.Pecas.Count > 1 &&
                                                !x.resultado.Avisos.Any(a => a.Contains("defasagem") || a.Contains("prumo"))))
            {
                var t = par.trecho;
                var arestas1 = PlanejadorFiadas.Arestas(r.F1, t.Parede, 1, r.P1.Fixas);
                var juntas = par.resultado.Juntas().Select(j => j + t.Inicio).ToList();
                if (t.Pontas.Porta.HasFlag(Bordas.Inicio) && juntas.Count > 0) juntas.RemoveAt(0);
                if (t.Pontas.Porta.HasFlag(Bordas.Fim) && juntas.Count > 0) juntas.RemoveAt(juntas.Count - 1);
                foreach (var j in juntas)
                    if (!arestas1.All(a => Math.Abs(a - j) >= 9.5)) { ruins++; _saida.WriteLine($"  prumo: {t.Nome} em {j:0.#}"); }
            }
            Assert.Equal(0, ruins);

            var svg = Environment.GetEnvironmentVariable("ALVMOD_SVG");
            if (!string.IsNullOrEmpty(svg))
            {
                var segs = PlantaRealTests.Carregar();
                Svg.Gravar(svg.Replace(".svg", "-f1.svg"), segs, r.Planta, Linhas(r.F1, r.P1));
                Svg.Gravar(svg.Replace(".svg", "-f2.svg"), segs, r.Planta, Linhas(r.F2, r.P2));
            }
        }

        private static (double x0, double y0, double x1, double y1) Retangulo(Parede w, double a, double b, double largura)
        {
            double m = largura / 2.0;
            return w.Horizontal ? (a, w.Coord - m, b, w.Coord + m) : (w.Coord - m, a, w.Coord + m, b);
        }

        private static IEnumerable<(double, double, double, double, string)> Linhas(
            List<(TrechoFiada trecho, ResultadoModulacao resultado)> fiada, PlanoFiada plano)
        {
            var pecas = fiada.SelectMany(x => x.resultado.Pecas.Select(p =>
                    (ret: Retangulo(x.trecho.Parede, x.trecho.Inicio + p.Inicio, x.trecho.Inicio + p.Fim, p.Peca.Largura), cod: p.Peca.Codigo)))
                .Concat(plano.Fixas.Select(f => (ret: Retangulo(f.Parede, f.Inicio, f.Fim, f.Peca.Largura), cod: f.Peca.Codigo)));
            foreach (var (ret, cod) in pecas)
            {
                var (x0, y0, x1, y1) = ret;
                string cor = cod switch { "39" => "#2a2", "19" => "#a2a", "34" => "#22f", "54" => "#f80", _ => "#f00" };
                yield return (x0, y0, x1, y0, cor);
                yield return (x1, y0, x1, y1, cor);
                yield return (x1, y1, x0, y1, cor);
                yield return (x0, y1, x0, y0, cor);
            }
        }
    }
}
