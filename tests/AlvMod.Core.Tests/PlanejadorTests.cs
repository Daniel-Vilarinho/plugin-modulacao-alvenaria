using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Planta;
using Xunit;

namespace AlvMod.Core.Tests
{
    /// <summary>
    /// Planejamento das duas fiadas do pavimento: amarração direta alternando a cada fiada, 54 nos T,
    /// portas com a medida nominal + 11 e deslocadas até fechar, janela que não corta, simetria.
    /// </summary>
    public class PlanejadorTests
    {
        private static (PlantaDetectada p, SolucaoPavimento sol, ModulacaoFiadas mod) Sala()
        {
            var p = Plantas.Detectar(Plantas.Sala());
            var (sol, mod) = Plantas.Modular(p);
            return (p, sol, mod);
        }

        private static string Quem(List<(string codigo, double x0, double y0, double x1, double y1, Parede parede)> pecas, double x, double y) =>
            pecas.Where(q => q.x0 - 0.1 <= x && x <= q.x1 + 0.1 && q.y0 - 0.1 <= y && y <= q.y1 + 0.1)
                 .Select(q => (q.parede.Horizontal ? "H" : "V") + q.codigo).FirstOrDefault();

        [Fact]
        public void Todos_os_trechos_fecham_com_juntas_de_1cm()
        {
            var (_, _, mod) = Sala();

            foreach (int f in new[] { 1, 2 })
                foreach (var (t, r) in mod.Fiada(f))
                {
                    Assert.True(r.Sucesso, $"F{f} {t}: {r.Mensagem}");
                    for (int i = 1; i < r.Pecas.Count; i++)
                        Assert.Equal(1, r.Pecas[i].Inicio - r.Pecas[i - 1].Fim, 1);
                }
        }

        [Fact]
        public void Em_cada_encontro_uma_parede_passa_e_inverte_na_outra_fiada()
        {
            var (p, sol, mod) = Sala();
            var f1 = Plantas.Pecas(sol, mod, 1);
            var f2 = Plantas.Pecas(sol, mod, 2);

            foreach (var e in p.Encontros)
            {
                var q1 = Quem(f1, e.X, e.Y);
                var q2 = Quem(f2, e.X, e.Y);
                Assert.NotNull(q1);
                Assert.NotNull(q2);
                Assert.NotEqual(q1[0], q2[0]);   // a direção que fica por cima troca entre as fiadas
            }
        }

        [Fact]
        public void Cada_T_tem_um_54_centrado_numa_das_fiadas()
        {
            var (p, sol, _) = Sala();

            foreach (var e in p.Encontros.Where(e => e.Tipo == TipoEncontro.T))
            {
                var fixos = new[] { 1, 2 }.SelectMany(f => sol.Plano(f).Fixas
                    .Where(x => Math.Abs((x.Inicio + x.Fim) / 2 - x.Parede.AoLongo(e.X, e.Y)) < 0.01 &&
                                Math.Abs(x.Parede.Transversal(e.X, e.Y) - x.Parede.Coord) < 0.01)).ToList();
                Assert.Single(fixos);
                Assert.Equal("54", fixos[0].Peca.Codigo);
            }
        }

        [Fact]
        public void Pecas_nao_se_sobrepoem()
        {
            var (_, sol, mod) = Sala();
            foreach (int f in new[] { 1, 2 })
            {
                var ps = Plantas.Pecas(sol, mod, f);
                for (int i = 0; i < ps.Count; i++)
                    for (int j = i + 1; j < ps.Count; j++)
                    {
                        double ix = Math.Min(ps[i].x1, ps[j].x1) - Math.Max(ps[i].x0, ps[j].x0);
                        double iy = Math.Min(ps[i].y1, ps[j].y1) - Math.Max(ps[i].y0, ps[j].y0);
                        Assert.False(ix > 0.5 && iy > 0.5, $"F{f}: {ps[i].codigo} e {ps[j].codigo} sobrepostos");
                    }
            }
        }

        [Fact]
        public void Porta_abre_91_e_anda_no_maximo_12_cm_para_fechar()
        {
            var (p, sol, mod) = Sala();
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 20);
            var porta = baixo.Vaos.Single();

            Assert.InRange(Math.Abs(porta.Deslocamento), 0, 12);
            var (a, b) = porta.Alvenaria(11);
            foreach (int f in new[] { 1, 2 })
            {
                var ps = Plantas.Pecas(sol, mod, f).Where(x => x.parede == baixo).ToList();
                Assert.Contains(ps, x => Math.Abs(x.x1 - a) < 0.6);   // ombreira esquerda
                Assert.Contains(ps, x => Math.Abs(x.x0 - b) < 0.6);   // ombreira direita
                Assert.DoesNotContain(ps, x => x.x0 < b - 0.5 && x.x1 > a + 0.5);
            }
        }

        [Fact]
        public void Porta_nunca_fica_a_menos_de_5_cm_da_outra_parede()
        {
            // Porta desenhada encostada na parede da esquerda: ela anda até sobrar a boneca de 5 cm.
            var p = Plantas.Detectar(Plantas.Sala());
            var porta = p.Paredes.Single(w => w.Horizontal && w.Coord < 20).Vaos.Single();
            porta.Inicio -= 37; porta.Fim -= 37;   // abertura de 91 a 2,5 cm da face
            var (sol, _) = Plantas.Modular(p);

            Assert.True(Plantas.MenorBoneca(p, new OpcoesFiadas()) >= 4.75, string.Join("; ", sol.Avisos));
        }

        [Fact]
        public void Janela_nao_corta_a_primeira_nem_a_segunda_fiada()
        {
            var (p, sol, mod) = Sala();
            var cima = p.Paredes.Single(w => w.Horizontal && w.Coord > 200);
            foreach (int f in new[] { 1, 2 })
            {
                var ps = Plantas.Pecas(sol, mod, f).Where(x => x.parede == cima).OrderBy(x => x.x0).ToList();
                Assert.Contains(ps, x => x.x0 < 300 && x.x1 > 300);   // tem bloco embaixo do meio da janela
            }
        }

        [Fact]
        public void Pontos_de_graute_em_todos_os_encontros()
        {
            var (p, sol, _) = Sala();

            Assert.Equal(p.Encontros.Count, sol.Grautes.Count);
        }

        [Fact]
        public void Apartamentos_espelhados_recebem_modulacao_espelhada()
        {
            var p = Plantas.Detectar(Plantas.Espelhada());
            var (sol, mod) = Plantas.Modular(p);

            Assert.Contains(sol.Avisos, a => a.Contains("simétrico"));
            foreach (int f in new[] { 1, 2 })
            {
                var ps = Plantas.Pecas(sol, mod, f);
                foreach (var q in ps)
                    Assert.Contains(ps, o => o.codigo == q.codigo && Math.Abs(o.x0 - (805 - q.x1)) < 0.6 && Math.Abs(o.y0 - q.y0) < 0.6);
            }
        }

        [Fact]
        public void Numeracao_por_tipo_da_o_mesmo_numero_a_paredes_iguais()
        {
            var p = Plantas.Detectar(Plantas.Espelhada());
            var (sol, mod) = Plantas.Modular(p);

            Numeracao.NumerarPorTipo(p.Paredes, w => Numeracao.Assinatura(sol, mod, w));

            var esquerda = p.Paredes.Single(w => !w.Horizontal && w.Coord < 20);
            var direita = p.Paredes.Single(w => !w.Horizontal && w.Coord > 790);
            Assert.Equal(esquerda.Nome, direita.Nome);
            Assert.True(p.Paredes.Select(w => w.Nome).Distinct().Count() < p.Paredes.Count);
        }

        [Fact]
        public void Cotas_internas_de_face_a_face_dos_blocos_com_as_aberturas()
        {
            var p = Plantas.Detectar(Plantas.Sala());
            var op = new OpcoesFiadas();
            PlanejadorFiadas.Planejar(p, 1, op);

            var cotas = Anotacoes.CotasInternas(p, op);

            // Parede de baixo, por dentro: da face da parede da esquerda (14,5) até a da interna (195,5), com a porta.
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 20);
            var cadeia = cotas.Single(c => c.Parede == baixo && c.Lado == 1 && c.Pontos.Count > 2);
            Assert.Equal(14.5, cadeia.Pontos.First(), 3);
            Assert.Equal(195.5, cadeia.Pontos.Last(), 3);
            var (a, b) = baixo.Vaos.Single().Alvenaria(11);
            Assert.Contains(cadeia.Pontos, x => Math.Abs(x - a) < 0.01);
            Assert.Contains(cadeia.Pontos, x => Math.Abs(x - b) < 0.01);
            Assert.Contains(cotas, c => c.Parede == baixo && c.Lado == 1 && c.Pontos.Count == 2 && c.Afastamento > cadeia.Afastamento);
            // Por fora (fachada) não tem cota interna.
            Assert.DoesNotContain(cotas, c => c.Parede == baixo && c.Lado == -1);
            // Janela de 100 entra com 101 na cota da parede de cima.
            var cima = p.Paredes.Single(w => w.Horizontal && w.Coord > 200);
            var cj = cotas.Single(c => c.Parede == cima && c.Lado == -1 && c.Pontos.Count > 2);
            Assert.Contains(cj.Pontos, x => Math.Abs(x - 249.5) < 0.01);
            Assert.Contains(cj.Pontos, x => Math.Abs(x - 350.5) < 0.01);
        }
    }
}
