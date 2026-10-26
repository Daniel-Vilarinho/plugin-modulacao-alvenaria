using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Planta;
using Xunit;

namespace AlvMod.Core.Tests
{
    /// <summary>Leitura da arquitetura: paredes pelo eixo, vãos, encontros, unidade e o que não é parede.</summary>
    public class DetectorTests
    {
        [Fact]
        public void Sala_tem_cinco_paredes_pelo_eixo_porta_janela_e_encontros()
        {
            var p = Plantas.Detectar(Plantas.Sala());

            Assert.Equal(5, p.Paredes.Count);
            Assert.All(p.Paredes, w => Assert.Equal(15, w.Espessura, 3));
            Assert.Equal(15, p.EspessuraTipica);
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 20);
            Assert.Equal(7.5, baixo.Coord, 3);
            var porta = Assert.Single(baixo.Vaos);
            Assert.Equal(TipoVao.Porta, porta.Tipo);
            Assert.Equal(80, porta.Largura, 3);
            Assert.Equal(-1, porta.LadoDobradica);                 // arco com centro do lado esquerdo
            var cima = p.Paredes.Single(w => w.Horizontal && w.Coord > 200);
            Assert.Equal(TipoVao.Janela, Assert.Single(cima.Vaos).Tipo);
            Assert.Equal(4, p.Encontros.Count(e => e.Tipo == TipoEncontro.L));
            Assert.Equal(2, p.Encontros.Count(e => e.Tipo == TipoEncontro.T));
        }

        [Fact]
        public void Porta_80_vira_abertura_de_91_e_janela_100_de_101()
        {
            var p = Plantas.Detectar(Plantas.Sala());
            var porta = p.Paredes.SelectMany(w => w.Vaos).Single(v => v.Tipo == TipoVao.Porta);
            var janela = p.Paredes.SelectMany(w => w.Vaos).Single(v => v.Tipo == TipoVao.Janela);

            var (a, b) = porta.Alvenaria(11);
            Assert.Equal(91, b - a, 3);
            Assert.Equal(porta.Centro, (a + b) / 2, 3);
            var (ja, jb) = janela.Alvenaria(1);
            Assert.Equal(101, jb - ja, 3);
        }

        [Fact]
        public void Degraus_de_escada_no_layer_de_parede_nao_viram_parede()
        {
            var s = Plantas.Sala();
            for (int i = 0; i < 6; i++)   // degraus de 30 cm desenhados como linhas no layer de parede
                Plantas.Linha(s, Plantas.Parede, 500 + 30 * i, 0, 500 + 30 * i, 120);

            var p = Plantas.Detectar(s);

            Assert.Equal(5, p.Paredes.Count);
        }

        [Fact]
        public void Parede_com_linha_no_meio_e_uma_parede_so()
        {
            var s = Plantas.Sala();
            Plantas.Linha(s, Plantas.Parede, 202.5, 15, 202.5, 285);   // linha do meio da parede interna

            var p = Plantas.Detectar(s);

            Assert.Equal(5, p.Paredes.Count);
            Assert.Contains(p.Paredes, w => !w.Horizontal && System.Math.Abs(w.Coord - 202.5) < 0.1 && System.Math.Abs(w.Espessura - 15) < 0.1);
        }

        [Fact]
        public void Janela_com_vidro_em_folhas_cobre_o_vao()
        {
            var s = Plantas.Sala().Where(x => x.Layer != Plantas.Janela).ToList();
            Plantas.Linha(s, Plantas.Janela, 250, 292, 300, 292);   // duas folhas, nenhuma cobre metade sozinha
            Plantas.Linha(s, Plantas.Janela, 296, 293, 350, 293);

            var p = Plantas.Detectar(s);

            var cima = p.Paredes.Single(w => w.Horizontal && w.Coord > 200);
            Assert.Equal(TipoVao.Janela, Assert.Single(cima.Vaos).Tipo);
        }

        [Fact]
        public void Arquitetura_em_escala_1_50_lida_como_metro_sugere_unidade_50()
        {
            // Arquitetura limpa real: 1 unidade = 50 cm. Lida com 100, as paredes dariam 30 cm.
            var desenho = Plantas.Sala().Select(x => new Segmento(x.Layer, x.X1 / 50, x.Y1 / 50, x.X2 / 50, x.Y2 / 50)
                { Raio = x.Raio / 50, Cx = x.Cx / 50, Cy = x.Cy / 50 }).ToList();
            var comoMetro = desenho.Select(x => new Segmento(x.Layer, x.X1 * 100, x.Y1 * 100, x.X2 * 100, x.Y2 * 100)
                { Raio = x.Raio * 100, Cx = x.Cx * 100, Cy = x.Cy * 100 }).ToList();

            var p = DetectorParedes.Detectar(comoMetro, Plantas.Opcoes());

            Assert.True(DetectorParedes.EspessuraSuspeita(p));
            Assert.Equal(50, DetectorParedes.SugerirFatorCm(comoMetro, 100, Plantas.Opcoes()));
        }

        [Fact]
        public void Parede_que_nao_e_alvenaria_sai_e_o_encontro_desaparece()
        {
            var op = Plantas.Opcoes();
            op.Exclusoes.Add((202.5, 150));   // parede interna em drywall

            var p = Plantas.Detectar(Plantas.Sala(), op);

            Assert.Equal(4, p.Paredes.Count);
            Assert.Single(p.Excluidas);
            Assert.DoesNotContain(p.Encontros, e => e.Tipo == TipoEncontro.T);
        }

        [Fact]
        public void Grade_modular_poe_os_eixos_a_distancias_multiplas_de_5()
        {
            var s = Plantas.Sala().Select(x => new Segmento(x.Layer, x.X1 * 1.002, x.Y1, x.X2 * 1.002, x.Y2)
                { Raio = x.Raio, Cx = x.Cx * 1.002, Cy = x.Cy }).ToList();   // desenho com 0,2% de erro
            var p = DetectorParedes.Detectar(s, Plantas.Opcoes());

            double ajuste = GradeModular.Ajustar(p);

            Assert.InRange(ajuste, 0.01, 2.5);
            var xs = p.Paredes.Where(w => !w.Horizontal).Select(w => w.Coord).OrderBy(c => c).ToList();
            Assert.All(xs, x => Assert.Equal(0, (x - xs[0]) % 5, 6));
        }

        [Fact]
        public void Simetria_do_pavimento_e_detectada()
        {
            var p = Plantas.Detectar(Plantas.Espelhada());

            var sim = Simetria.Detectar(p);

            Assert.Contains("x = 402,5", string.Join(";", sim.Eixos).Replace('.', ','));
            Assert.Contains(sim.Portas, g => g.Count == 2);
        }
    }
}
