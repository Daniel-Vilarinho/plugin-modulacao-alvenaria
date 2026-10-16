using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using AlvMod.Core.Planta;
using Xunit;

namespace AlvMod.Core.Tests
{
    /// <summary>
    /// Planta pequena desenhada à mão (cm): cômodo 400 × 300 com paredes de 15 cm pelas faces, uma parede
    /// interna vertical em x = 200 (dois encontros em T) e uma porta de 80 cm na parede de baixo (x 60–140).
    /// </summary>
    public class PlantaSinteticaTests
    {
        private static List<Segmento> Planta()
        {
            var s = new List<Segmento>();
            void P(double x1, double y1, double x2, double y2) => s.Add(new Segmento("PAREDE", x1, y1, x2, y2));
            // Faces externas (com a porta na de baixo).
            P(0, 0, 60, 0); P(140, 0, 400, 0); P(400, 0, 400, 300); P(400, 300, 0, 300); P(0, 300, 0, 0);
            // Faces internas (cortadas pela parede interna e pela porta).
            P(15, 15, 60, 15); P(140, 15, 192.5, 15); P(207.5, 15, 385, 15); P(385, 15, 385, 285);
            P(385, 285, 207.5, 285); P(192.5, 285, 15, 285); P(15, 285, 15, 15);
            // Parede interna.
            P(192.5, 15, 192.5, 285); P(207.5, 15, 207.5, 285);
            // Porta: marcos e folha.
            s.Add(new Segmento("PORTA", 60, 0, 60, 15));
            s.Add(new Segmento("PORTA", 140, 0, 140, 15));
            s.Add(new Segmento("PORTA", 60, 7.5, 140, 7.5));
            return s;
        }

        private static PlantaDetectada Detectar()
        {
            var op = new OpcoesDeteccao { LayersParede = { "PAREDE" }, LayersPorta = { "PORTA" } };
            var planta = DetectorParedes.Detectar(Planta(), op);
            Numeracao.Numerar(planta);
            return planta;
        }

        [Fact]
        public void Detecta_cinco_paredes_pelo_eixo_com_porta()
        {
            var p = Detectar();

            Assert.Equal(5, p.Paredes.Count);
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 10);
            Assert.Equal(7.5, baixo.Coord, 3);
            Assert.Equal(15, baixo.Espessura, 3);
            var porta = Assert.Single(baixo.Vaos);
            Assert.Equal(TipoVao.Porta, porta.Tipo);
            Assert.Equal(60, porta.Inicio, 3);
            Assert.Equal(140, porta.Fim, 3);
            Assert.Contains(p.Paredes, w => !w.Horizontal && System.Math.Abs(w.Coord - 200) < 0.01);
        }

        [Fact]
        public void Encontros_quatro_L_e_dois_T_com_eixos_estendidos_ate_o_cruzamento()
        {
            var p = Detectar();

            Assert.Equal(4, p.Encontros.Count(e => e.Tipo == TipoEncontro.L));
            Assert.Equal(2, p.Encontros.Count(e => e.Tipo == TipoEncontro.T));
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 10);
            Assert.Equal(7.5, baixo.Inicio, 3);   // estendida até o eixo da parede da esquerda
            Assert.Equal(392.5, baixo.Fim, 3);
        }

        [Fact]
        public void Numeracao_horizontais_de_cima_para_baixo_depois_verticais_da_esquerda_para_a_direita()
        {
            var p = Detectar();

            Assert.Equal("PAREDE 1", p.Paredes.Single(w => w.Horizontal && w.Coord > 200).Nome);
            Assert.Equal("PAREDE 2", p.Paredes.Single(w => w.Horizontal && w.Coord < 10).Nome);
            Assert.Equal("PAREDE 3", p.Paredes.Single(w => !w.Horizontal && w.Coord < 10).Nome);
            Assert.Equal("PAREDE 4", p.Paredes.Single(w => !w.Horizontal && System.Math.Abs(w.Coord - 200) < 1).Nome);
            Assert.Equal("PAREDE 5", p.Paredes.Single(w => !w.Horizontal && w.Coord > 300).Nome);
        }

        [Fact]
        public void Fiada_1_parede_maior_passa_nos_cantos_e_porta_soma_folga_de_marco()
        {
            var p = Detectar();
            var trechos = PlanejadorFiadas.Trechos(p, 1, new OpcoesFiadas())
                .Where(t => t.Parede.Nome == "PAREDE 2").OrderBy(t => t.Inicio).ToList();

            // Passa nos cantos: vai até a face externa (eixo ∓ 7). Porta 60–140 + 5,5 de cada lado.
            Assert.Equal(2, trechos.Count);
            Assert.Equal(0.5, trechos[0].Inicio, 3);
            Assert.Equal(54.5, trechos[0].Fim, 3);
            Assert.Equal(145.5, trechos[1].Inicio, 3);
            Assert.Equal(399.5, trechos[1].Fim, 3);
            Assert.Equal("PAREDE 2-A", trechos[0].Nome);
            Assert.Equal("PAREDE 2-B", trechos[1].Nome);
            Assert.True(trechos[0].Bordas.HasFlag(Bordas.Fim));   // encosta na porta: sem compensador
            Assert.True(trechos[1].Bordas.HasFlag(Bordas.Inicio));
        }

        [Fact]
        public void Fiada_2_alterna_a_parede_que_passa_nos_cantos_e_no_T()
        {
            var p = Detectar();
            var trechos = PlanejadorFiadas.Trechos(p, 2, new OpcoesFiadas())
                .Where(t => t.Parede.Nome == "PAREDE 2").OrderBy(t => t.Inicio).ToList();

            // Recua nos cantos (eixo ± 8) e é cortada no T, onde a parede interna passa.
            Assert.Equal(3, trechos.Count);
            Assert.Equal(15.5, trechos[0].Inicio, 3);
            Assert.Equal(54.5, trechos[0].Fim, 3);
            Assert.Equal(145.5, trechos[1].Inicio, 3);
            Assert.Equal(192, trechos[1].Fim, 3);
            Assert.Equal(208, trechos[2].Inicio, 3);
            Assert.Equal(384.5, trechos[2].Fim, 3);

            var interna = PlanejadorFiadas.Trechos(p, 2, new OpcoesFiadas()).Single(t => t.Parede.Nome == "PAREDE 4");
            Assert.Equal(0.5, interna.Inicio, 3);   // passa até a face externa das paredes de cima e de baixo
            Assert.Equal(299.5, interna.Fim, 3);
            Assert.Equal(Bordas.Ambas, interna.Encontros);
        }

        [Fact]
        public void Pavimento_modula_sem_sobreposicao_e_2a_fiada_sem_prumo()
        {
            var p = Detectar();
            var modulador = new Modulador(CatalogoPadrao.Familia39Concreto());
            var op = new OpcoesFiadas();
            var f1 = PlanejadorFiadas.Trechos(p, 1, op)
                .Select(t => (trecho: t, resultado: modulador.PrimeiraFiada(t.Comprimento, t.Bordas, t.Encontros))).ToList();
            var f2 = PlanejadorFiadas.Trechos(p, 2, op).Select(t =>
                (trecho: t, resultado: modulador.SegundaFiada(t.Comprimento,
                    PlanejadorFiadas.Arestas(f1, t.Parede).Select(a => a - t.Inicio), t.Bordas, t.Encontros))).ToList();

            Assert.All(f1, x => Assert.True(x.resultado.Pecas.Count > 0, x.trecho.ToString()));
            foreach (var (t, r) in f2.Where(x => x.resultado.Pecas.Count > 1))
            {
                var arestas1 = PlanejadorFiadas.Arestas(f1, t.Parede);
                foreach (var j in r.Juntas(1).Select(j => j + t.Inicio))
                    Assert.All(arestas1, a => Assert.True(System.Math.Abs(a - j) >= 9.99, $"{t.Nome} prumo em {j}"));
            }
        }
    }
}
