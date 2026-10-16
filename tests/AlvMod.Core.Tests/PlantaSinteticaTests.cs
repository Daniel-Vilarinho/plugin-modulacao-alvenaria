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
    /// Planta pequena desenhada à mão (cm): cômodo 400 × 300 com paredes de 15 cm pelas faces, uma parede
    /// interna vertical em x = 200 (dois encontros em T) e uma porta de 80 cm na parede de baixo (x 60–140).
    /// </summary>
    public class PlantaSinteticaTests
    {
        private static List<Segmento> Planta(bool comArco = false)
        {
            var s = new List<Segmento>();
            void P(double x1, double y1, double x2, double y2) => s.Add(new Segmento("PAREDE", x1, y1, x2, y2));
            // Faces externas (com a porta na de baixo e uma janela de 100 cm em x 250–350 na de cima).
            P(0, 0, 60, 0); P(140, 0, 400, 0); P(400, 0, 400, 300); P(400, 300, 350, 300); P(250, 300, 0, 300); P(0, 300, 0, 0);
            // Faces internas (cortadas pela parede interna, pela porta e pela janela).
            P(15, 15, 60, 15); P(140, 15, 192.5, 15); P(207.5, 15, 385, 15); P(385, 15, 385, 285);
            P(385, 285, 350, 285); P(250, 285, 207.5, 285); P(192.5, 285, 15, 285); P(15, 285, 15, 15);
            // Janela: vidro com o tamanho do vão.
            s.Add(new Segmento("JANELA", 250, 289, 350, 289));
            s.Add(new Segmento("JANELA", 250, 296, 350, 296));
            // Porta de folha de 70 cm (abertura desenhada de 80, com o marco): arco com centro na dobradiça.
            if (comArco)
            {
                s.Add(new Segmento("PORTA", 130, 7.5, 109.5, 57) { Raio = 70, Cx = 60, Cy = 7.5 });
                s.Add(new Segmento("PORTA", 109.5, 57, 60, 77.5) { Raio = 70, Cx = 60, Cy = 7.5 });
            }
            // Parede interna.
            P(192.5, 15, 192.5, 285); P(207.5, 15, 207.5, 285);
            // Porta: marcos e folha.
            s.Add(new Segmento("PORTA", 60, 0, 60, 15));
            s.Add(new Segmento("PORTA", 140, 0, 140, 15));
            s.Add(new Segmento("PORTA", 60, 7.5, 140, 7.5));
            return s;
        }

        private static PlantaDetectada Detectar(bool comArco = false)
        {
            var op = new OpcoesDeteccao { LayersParede = { "PAREDE" }, LayersPorta = { "PORTA" }, LayersJanela = { "JANELA" } };
            var planta = DetectorParedes.Detectar(Planta(comArco), op);
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
            Assert.True(trechos[0].Pontas.SemCompensador.HasFlag(Bordas.Fim));   // encosta na porta: sem compensador
            Assert.True(trechos[1].Pontas.SemCompensador.HasFlag(Bordas.Inicio));
            Assert.True(trechos[0].Pontas.Porta.HasFlag(Bordas.Fim));            // e termina em 19
            Assert.True(trechos[1].Pontas.Porta.HasFlag(Bordas.Inicio));
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
            Assert.Equal(Bordas.Ambas, interna.Pontas.Passa34);     // atravessa o T: termina em 34 nas duas pontas
            Assert.Equal(Bordas.Ambas, interna.Pontas.Encontro);    // e o 19 não entra ali
        }

        [Fact]
        public void Pavimento_modula_sem_sobreposicao_e_2a_fiada_sem_prumo()
        {
            var p = Detectar();
            var modulador = new Modulador(CatalogoPadrao.Familia39Concreto());
            var op = new OpcoesFiadas();
            var f1 = PlanejadorFiadas.Trechos(p, 1, op)
                .Select(t => (trecho: t, resultado: modulador.PrimeiraFiada(t.Comprimento, t.Pontas))).ToList();
            var f2 = PlanejadorFiadas.Trechos(p, 2, op).Select(t =>
                (trecho: t, resultado: modulador.SegundaFiada(t.Comprimento,
                    PlanejadorFiadas.Arestas(f1, t.Parede).Select(a => a - t.Inicio), t.Pontas))).ToList();

            Assert.All(f1, x => Assert.True(x.resultado.Pecas.Count > 0, x.trecho.ToString()));
            foreach (var (t, r) in f2.Where(x => x.resultado.Pecas.Count > 1 && !x.resultado.Avisos.Any(av => av.Contains("defasagem") || av.Contains("prumo"))))
            {
                var arestas1 = PlanejadorFiadas.Arestas(f1, t.Parede);
                var juntas = r.Juntas(1).Select(j => j + t.Inicio).ToList();
                // A junta ao lado do 19 da porta é igual nas duas fiadas (exceção combinada com o engenheiro).
                if (t.Pontas.Porta.HasFlag(Bordas.Inicio) && juntas.Count > 0) juntas.RemoveAt(0);
                if (t.Pontas.Porta.HasFlag(Bordas.Fim) && juntas.Count > 0) juntas.RemoveAt(juntas.Count - 1);
                foreach (var j in juntas)
                    Assert.All(arestas1, a => Assert.True(System.Math.Abs(a - j) >= 9.99, $"{t.Nome} prumo em {j}"));
            }
        }
    
        [Fact]
        public void Porta_sem_arco_usa_o_vao_desenhado_mais_11()
        {
            var p = Detectar();
            var porta = p.Paredes.SelectMany(w => w.Vaos).Single(v => v.Tipo == TipoVao.Porta);

            Assert.False(porta.TamanhoLido);
            var (a, b) = porta.Alvenaria(11);
            Assert.Equal(54.5, a, 3);
            Assert.Equal(145.5, b, 3);
        }

        [Fact]
        public void Porta_com_arco_e_lida_pelo_tamanho_da_folha_mais_11_centrada_no_vao()
        {
            var p = Detectar(comArco: true);
            var porta = p.Paredes.SelectMany(w => w.Vaos).Single(v => v.Tipo == TipoVao.Porta);

            Assert.True(porta.TamanhoLido);
            Assert.Equal(70, porta.Tamanho, 3);
            var (a, b) = porta.Alvenaria(11);
            Assert.Equal(59.5, a, 3);     // 100 - (70 + 11) / 2
            Assert.Equal(140.5, b, 3);

            var trechos = PlanejadorFiadas.Trechos(p, 1, new OpcoesFiadas())
                .Where(t => t.Parede.Horizontal && t.Parede.Coord < 10).OrderBy(t => t.Inicio).ToList();
            Assert.Equal(59.5, trechos[0].Fim, 3);
            Assert.Equal(140.5, trechos[1].Inicio, 3);
        }

        [Fact]
        public void Janela_nao_corta_a_fiada_mas_entra_na_cota_com_vao_mais_6()
        {
            var p = Detectar();
            var topo = p.Paredes.Single(w => w.Horizontal && w.Coord > 200);
            var janela = Assert.Single(topo.Vaos);

            Assert.Equal(TipoVao.Janela, janela.Tipo);
            Assert.Equal(100, janela.Tamanho, 3);        // vidro = vão
            var op = new OpcoesFiadas();
            var trechos = PlanejadorFiadas.Trechos(p, 1, op).Where(t => t.Parede == topo).ToList();
            Assert.Single(trechos);                        // a janela não corta a 1ª fiada

            var pontos = PlanejadorFiadas.PontosDeCota(topo, trechos, op);
            Assert.Contains(pontos, x => Math.Abs(x - 247) < 0.01);   // 300 - (100 + 6) / 2
            Assert.Contains(pontos, x => Math.Abs(x - 353) < 0.01);
            Assert.Equal(4, pontos.Count);                 // fim da parede, janela (2 pontos), início
        }
    
        private static OpcoesFiadas Com54() =>
            new OpcoesFiadas { BlocoEncontro = CatalogoPadrao.Familia39Concreto().Buscar("54") };

        [Fact]
        public void T_recebe_54_centrado_na_parede_continua_na_fiada_em_que_ela_passa()
        {
            var p = Detectar();
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 10);

            var f1 = PlanejadorFiadas.Planejar(p, 1, Com54());
            var fixa = Assert.Single(f1.Fixas, f => f.Parede == baixo);
            Assert.Equal("54", fixa.Peca.Codigo);
            Assert.Equal(200, (fixa.Inicio + fixa.Fim) / 2, 3);          // centrado no encontro (x = 200)

            // Os trechos vizinhos ficam a 1 cm do 54 e começam/terminam com 39.
            var vizinhos = f1.Trechos.Where(t => t.Parede == baixo).OrderBy(t => t.Inicio).ToList();
            var antes = vizinhos.Single(t => Math.Abs(t.Fim - (fixa.Inicio - 1)) < 0.01);
            var depois = vizinhos.Single(t => Math.Abs(t.Inicio - (fixa.Fim + 1)) < 0.01);
            Assert.True(antes.Pontas.Exige39.HasFlag(Bordas.Fim));
            Assert.True(depois.Pontas.Exige39.HasFlag(Bordas.Inicio));

            // Na 2ª fiada a parede de baixo é cortada em volta da interna (que passa com 34) e não tem 54.
            var f2 = PlanejadorFiadas.Planejar(p, 2, Com54());
            Assert.DoesNotContain(f2.Fixas, f => f.Parede == baixo);
            var interna = f2.Trechos.Single(t => t.Parede.Nome == "PAREDE 4");
            Assert.Equal(Bordas.Ambas, interna.Pontas.Passa34);
        }

        [Fact]
        public void T_com_54_deixa_meio_bloco_de_defasagem_entre_as_fiadas_na_parede_continua()
        {
            var p = Detectar();
            var op = Com54();
            var m = new Modulador(CatalogoPadrao.Familia39Concreto());
            var baixo = p.Paredes.Single(w => w.Horizontal && w.Coord < 10);

            var p1 = PlanejadorFiadas.Planejar(p, 1, op);
            var f1 = p1.Trechos.Select(t => (trecho: t, resultado: m.PrimeiraFiada(t.Comprimento, t.Pontas))).ToList();
            var arestas1 = PlanejadorFiadas.Arestas(f1, baixo, 1, p1.Fixas);

            var p2 = PlanejadorFiadas.Planejar(p, 2, op);
            var cortados = p2.Trechos.Where(t => t.Parede == baixo && t.Fim > 150).OrderBy(t => t.Inicio).ToList();
            // As pontas do corte da 2ª fiada (200 ± 8) ficam a 20 cm das juntas do 54 da 1ª (200 ± 27,5).
            Assert.Contains(arestas1, a => Math.Abs(a - 172.5) < 0.6);
            Assert.Contains(arestas1, a => Math.Abs(a - 227.5) < 0.6);
            Assert.Contains(cortados, t => Math.Abs(t.Fim - 192) < 0.01);
            Assert.Contains(cortados, t => Math.Abs(t.Inicio - 208) < 0.01);
        }
    }
}
