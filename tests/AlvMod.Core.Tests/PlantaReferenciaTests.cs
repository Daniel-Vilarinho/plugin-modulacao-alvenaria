using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Planta;
using Xunit;
using Xunit.Abstractions;

namespace AlvMod.Core.Tests
{
    /// <summary>
    /// Planta real de referência: a arquitetura limpa que o engenheiro recebe e a modulação que ele fez à mão,
    /// exportadas em cm para DadosLocais, com os layers, as paredes que não são alvenaria e os números esperados
    /// em referencia-caso.json. Nada disso vai para o repositório público: sem os arquivos, os testes passam
    /// sem fazer nada.
    ///
    /// Com ALVMOD_DUMP=caminho.json grava a detecção e a modulação do plugin para os scripts de comparação.
    /// </summary>
    public class PlantaReferenciaTests
    {
        private readonly ITestOutputHelper _saida;
        public PlantaReferenciaTests(ITestOutputHelper saida) => _saida = saida;

        private static string Local(string arquivo) =>
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "DadosLocais", arquivo);

        private sealed class Caso
        {
            public PlantaDetectada Planta;
            public SolucaoPavimento Solucao;
            public ModulacaoFiadas Modulacao;
            public double Ajuste;
            public List<(int f, string c, double x0, double y0, double x1, double y1)> Engenheiro;
            public JsonElement Esperado;
        }

        private static readonly Lazy<Caso> Cache = new Lazy<Caso>(Montar);

        private static Caso Carregar() => Cache.Value;

        private static Caso Montar()
        {
            if (!File.Exists(Local("referencia-caso.json"))) return null;
            var caso = JsonDocument.Parse(File.ReadAllText(Local("referencia-caso.json"))).RootElement;
            List<string> Lista(string nome) => caso.GetProperty(nome).EnumerateArray().Select(e => e.GetString()).ToList();
            using var doc = JsonDocument.Parse(File.ReadAllText(Local(caso.GetProperty("arquitetura").GetString())));
            var segs = doc.RootElement.GetProperty("segs").EnumerateArray().Select(e =>
            {
                var s = new Segmento(e.GetProperty("l").GetString(),
                    e.GetProperty("x1").GetDouble(), e.GetProperty("y1").GetDouble(),
                    e.GetProperty("x2").GetDouble(), e.GetProperty("y2").GetDouble());
                if (e.TryGetProperty("r", out var r))
                {
                    s.Raio = r.GetDouble(); s.Cx = e.GetProperty("cx").GetDouble(); s.Cy = e.GetProperty("cy").GetDouble();
                }
                return s;
            }).ToList();
            var op = new OpcoesDeteccao();
            foreach (var l in Lista("layersParede")) op.LayersParede.Add(l);
            foreach (var l in Lista("layersPorta")) op.LayersPorta.Add(l);
            foreach (var l in Lista("layersJanela")) op.LayersJanela.Add(l);
            foreach (var x in caso.GetProperty("exclusoes").EnumerateArray())
                op.Exclusoes.Add((x[0].GetDouble(), x[1].GetDouble()));

            var c = new Caso { Planta = DetectorParedes.Detectar(segs, op), Esperado = caso.GetProperty("esperado").Clone() };
            c.Ajuste = GradeModular.Ajustar(c.Planta);
            Numeracao.Numerar(c.Planta);
            var familia = CatalogoPadrao.Familia39Concreto();
            var m = new Modulador(familia);
            var opF = new OpcoesFiadas { BlocoEncontro = familia.Buscar("54") };
            c.Solucao = PlanejadorFiadas.Resolver(c.Planta, m, opF);
            c.Modulacao = ModulacaoPavimento.Modular(c.Solucao, m, opF);

            using var eng = JsonDocument.Parse(File.ReadAllText(Local(caso.GetProperty("engenheiro").GetString())));
            c.Engenheiro = eng.RootElement.GetProperty("pecas").EnumerateArray()
                .Select(p => (p.GetProperty("f").GetInt32(), p.GetProperty("c").GetString(),
                              p.GetProperty("x0").GetDouble(), p.GetProperty("y0").GetDouble(),
                              p.GetProperty("x1").GetDouble(), p.GetProperty("y1").GetDouble()))
                .ToList();
            return c;
        }

        private static List<(string c, double x0, double y0, double x1, double y1)> Nossas(Caso c, int fiada)
        {
            var r = new List<(string, double, double, double, double)>();
            void Add(Parede w, double a, double b, string cod) =>
                r.Add(w.Horizontal ? (cod, a, w.Coord - 7, b, w.Coord + 7) : (cod, w.Coord - 7, a, w.Coord + 7, b));
            foreach (var (t, res) in c.Modulacao.Fiada(fiada))
                foreach (var p in res.Pecas) Add(t.Parede, t.Inicio + p.Inicio, t.Inicio + p.Fim, p.Peca.Codigo);
            foreach (var f in c.Solucao.Plano(fiada).Fixas) Add(f.Parede, f.Inicio, f.Fim, f.Peca.Codigo);
            return r;
        }

        [Fact]
        public void Le_a_arquitetura_limpa_com_unidade_50_cm()
        {
            var c = Carregar();
            if (c == null) return;

            _saida.WriteLine($"{c.Planta.Paredes.Count} paredes, espessura {c.Planta.EspessuraTipica} cm, grade ±{c.Ajuste:0.##} cm");
            double espessura = c.Esperado.GetProperty("espessura").GetDouble();
            Assert.Equal(espessura, c.Planta.EspessuraTipica);
            Assert.All(c.Planta.Paredes, w => Assert.Equal(espessura, w.Espessura, 0));
            Assert.InRange(c.Ajuste, 0, 1.5);
            foreach (var tipo in new[] { TipoEncontro.L, TipoEncontro.T, TipoEncontro.X })
                Assert.Equal(c.Esperado.GetProperty(tipo.ToString()).GetInt32(), c.Planta.Encontros.Count(e => e.Tipo == tipo));
            var simetria = c.Esperado.GetProperty("simetria").EnumerateArray().Select(e => "simétrico em " + e.GetString()).ToList();
            Assert.Contains(c.Solucao.Avisos, a => simetria.Any(a.Contains));
        }

        [Fact]
        public void Modulacao_reproduz_a_do_engenheiro()
        {
            var c = Carregar();
            if (c == null) return;

            int iguais = 0, total = 0;
            foreach (int f in new[] { 1, 2 })
            {
                var nossas = Nossas(c, f);
                foreach (var e in c.Engenheiro.Where(p => p.f == f))
                {
                    total++;
                    if (nossas.Any(n => n.c == e.c && Math.Abs(n.x0 - e.x0) <= 1.5 && Math.Abs(n.y0 - e.y0) <= 1.5 &&
                                        Math.Abs(n.x1 - e.x1) <= 1.5 && Math.Abs(n.y1 - e.y1) <= 1.5))
                        iguais++;
                }
            }
            _saida.WriteLine($"{iguais} de {total} peças do engenheiro reproduzidas ({100.0 * iguais / total:0}%)");
            // O que falta vem quase todo de uma porta que não está na arquitetura limpa.
            Assert.True(iguais >= 0.85 * total, $"{iguais}/{total}");
        }

        [Fact]
        public void Quem_passa_em_cada_encontro_e_o_mesmo_que_o_engenheiro_escolheu()
        {
            var c = Carregar();
            if (c == null) return;

            int iguais = 0;
            foreach (var est in c.Solucao.Estados)
            {
                var e = est.Encontro;
                var p = c.Engenheiro.FirstOrDefault(q => q.f == 1 && q.x0 - 1 <= e.X && e.X <= q.x1 + 1 && q.y0 - 1 <= e.Y && e.Y <= q.y1 + 1);
                if (p.c == null) continue;
                double l = double.Parse(p.c);
                bool vertical = Math.Abs((p.y1 - p.y0) - l) < 1.5;
                if (vertical == est.VerticalPorCima) iguais++;
                else _saida.WriteLine($"diferente: {e}");
            }
            _saida.WriteLine($"{iguais} de {c.Solucao.Estados.Count} encontros iguais");
            // Os 4 cantos do cômodo cuja porta falta na arquitetura limpa ficam diferentes.
            Assert.True(iguais >= c.Solucao.Estados.Count - 4);
        }

        [Fact]
        public void Segunda_fiada_sem_junta_a_prumo_e_portas_com_91()
        {
            var c = Carregar();
            if (c == null) return;

            foreach (int f in new[] { 1, 2 })
                Assert.All(c.Modulacao.Fiada(f), x => Assert.True(x.resultado.Sucesso, $"F{f} {x.trecho}: {x.resultado.Mensagem}"));

            var meioBloco = 0;
            var juntas = 0;
            foreach (var w in c.Planta.Paredes)
            {
                // Juntas de verdade da 1ª fiada (entre duas peças a 1 cm), sem as pontas dos trechos.
                var pecas1 = c.Modulacao.Fiada1.Where(x => x.trecho.Parede == w)
                    .SelectMany(x => x.resultado.Pecas.Select(p => (a: x.trecho.Inicio + p.Inicio, b: x.trecho.Inicio + p.Fim)))
                    .Concat(c.Solucao.Fiada1.Fixas.Where(x => x.Parede == w).Select(x => (a: x.Inicio, b: x.Fim)))
                    .OrderBy(x => x.a).ToList();
                var j1 = Enumerable.Range(0, Math.Max(0, pecas1.Count - 1))
                    .Where(i => pecas1[i + 1].a - pecas1[i].b < 1.6)
                    .Select(i => (pecas1[i].b + pecas1[i + 1].a) / 2).ToList();
                foreach (var (t, r) in c.Modulacao.Fiada2.Where(x => x.trecho.Parede == w))
                    foreach (var j in r.Juntas())
                    {
                        double d = j1.Count == 0 ? 20 : j1.Min(x => Math.Abs(x - (j + t.Inicio)));
                        Assert.True(d >= 10 - 1e-6, $"{w.Nome}: junta a {d:0.#} cm da de baixo");
                        juntas++;
                        if (d >= 20 - 1e-6) meioBloco++;
                    }
            }
            _saida.WriteLine($"{meioBloco} de {juntas} juntas da 2ª fiada a meio bloco");
            Assert.True(meioBloco >= 0.75 * juntas);   // o engenheiro: 302 de 358

            foreach (var v in c.Planta.Paredes.SelectMany(w => w.Vaos).Where(v => v.Tipo == TipoVao.Porta))
            {
                var (a, b) = v.Alvenaria(11);
                Assert.Contains(Math.Round(b - a), new double[] { 81, 91 });
            }
            Assert.True(Plantas.MenorBoneca(c.Planta, new OpcoesFiadas()) >= 4.75);
        }

        [Fact]
        public void Grava_a_comparacao_quando_pedido()
        {
            var destino = Environment.GetEnvironmentVariable("ALVMOD_DUMP");
            if (string.IsNullOrEmpty(destino)) return;
            var c = Carregar();
            if (c == null) return;

            object Ret(Parede w, double a, double b, string codigo, string trecho) => new
            {
                parede = w.Nome, trecho, codigo,
                x0 = w.Horizontal ? a : w.Coord - 7, x1 = w.Horizontal ? b : w.Coord + 7,
                y0 = w.Horizontal ? w.Coord - 7 : a, y1 = w.Horizontal ? w.Coord + 7 : b
            };
            IEnumerable<object> Pecas(int f) =>
                c.Modulacao.Fiada(f).SelectMany(x => x.resultado.Pecas.Select(pc =>
                        Ret(x.trecho.Parede, x.trecho.Inicio + pc.Inicio, x.trecho.Inicio + pc.Fim, pc.Peca.Codigo, x.trecho.Nome)))
                    .Concat(c.Solucao.Plano(f).Fixas.Select(fx => Ret(fx.Parede, fx.Inicio, fx.Fim, fx.Peca.Codigo, "fixa")));
            IEnumerable<object> Trechos(int f) =>
                c.Modulacao.Fiada(f).Select(x => new
                {
                    parede = x.trecho.Parede.Nome, nome = x.trecho.Nome, inicio = x.trecho.Inicio, fim = x.trecho.Fim,
                    sucesso = x.resultado.Sucesso, sobra = x.resultado.Sobra, msg = x.resultado.Mensagem,
                    pecas = string.Join(" ", x.resultado.Pecas.Select(pc => pc.Peca.Codigo)),
                    ti = x.trecho.TipoInicio.ToString(), tf = x.trecho.TipoFim.ToString(), avisos = x.resultado.Avisos
                });
            var dump = new
            {
                paredes = c.Planta.Paredes.Select(w => new
                {
                    nome = w.Nome, h = w.Horizontal, coord = w.Coord, inicio = w.Inicio, fim = w.Fim, esp = w.Espessura,
                    vaos = w.Vaos.Select(v => new { tipo = v.Tipo.ToString(), v.Inicio, v.Fim, v.Tamanho, v.Deslocamento })
                }),
                encontros = c.Planta.Encontros.Select(e => new
                {
                    tipo = e.Tipo.ToString(), e.X, e.Y,
                    passantes = e.Passantes.Select(w => w.Nome), terminais = e.Terminais.Select(w => w.Nome)
                }),
                avisos = c.Planta.Avisos.Concat(c.Solucao.Avisos),
                ignoradas = c.Planta.Ignoradas,
                estados = c.Solucao.Estados.Select(e => new { tipo = e.Encontro.Tipo.ToString(), e.Encontro.X, e.Encontro.Y, vert = e.VerticalPorCima }),
                grautes = c.Solucao.Grautes.Select(g => new { g.X, g.Y }),
                f1 = Pecas(1), f2 = Pecas(2),
                trechos1 = Trechos(1), trechos2 = Trechos(2)
            };
            File.WriteAllText(destino, JsonSerializer.Serialize(dump));
        }
    }
}
