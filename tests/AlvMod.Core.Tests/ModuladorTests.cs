using System;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using Xunit;

namespace AlvMod.Core.Tests
{
    /// <summary>
    /// Motor de modulação de um trecho. As regras são as da modulação à mão do engenheiro:
    /// 39 em tudo, 34 juntos na ponta do encontro, 19 só na ombreira de porta, bolacha só encostada no
    /// 54/parede que atravessa (nunca no quadrado do canto nem no meio), juntas de 1 cm.
    /// </summary>
    public class ModuladorTests
    {
        private static Modulador NovoModulador() => new Modulador(CatalogoPadrao.Familia39Concreto());

        private static Pontas P(Bordas porta = Bordas.Nenhuma, Bordas encosta = Bordas.Nenhuma, Bordas passa = Bordas.Nenhuma,
            Bordas ajuste = Bordas.Nenhuma, Bordas canto = Bordas.Nenhuma) => new Pontas
        {
            Porta = porta,
            Encontro = encosta | passa,
            Passa = passa,
            SemCompensador = porta | passa,
            Ajuste = ajuste,
            Canto = canto
        };

        private static string Seq(ResultadoModulacao r) => string.Join(" ", r.Pecas.Select(p => p.Peca.Codigo));

        [Fact]
        public void Passo_da_familia_39_e_5cm()
        {
            Assert.Equal(5, NovoModulador().Passo);
        }

        [Fact]
        public void Quinhentos_e_noventa_e_nove_entre_encontros_sao_quinze_blocos_de_39_a_1cm()
        {
            var r = NovoModulador().PrimeiraFiada(599, P(encosta: Bordas.Ambas));

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(15, r.Pecas.Count);
            Assert.All(r.Pecas, p => Assert.Equal("39", p.Peca.Codigo));
            for (int i = 1; i < r.Pecas.Count; i++)
                Assert.Equal(1, r.Pecas[i].Inicio - r.Pecas[i - 1].Fim, 6);
            Assert.Equal(599, r.Pecas.Last().Fim, 6);
        }

        [Fact]
        public void Trinta_e_quatro_ficam_juntos_na_ponta_de_ajuste_e_nao_no_meio()
        {
            // Pilar da escada do engenheiro: entre dois 54, 259 cm = quatro 34 + três 39, os 34 juntos.
            var r = NovoModulador().PrimeiraFiada(259, P(encosta: Bordas.Ambas, ajuste: Bordas.Inicio));

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("34 34 34 34 39 39 39", Seq(r));
        }

        [Fact]
        public void Prefere_quatro_34_a_duas_bolachas()
        {
            var r = NovoModulador().PrimeiraFiada(259, P(encosta: Bordas.Ambas, ajuste: Bordas.Fim));

            Assert.Equal("39 39 39 34 34 34 34", Seq(r));
            Assert.DoesNotContain(r.Pecas, p => p.Peca.EhCompensador);
        }

        [Fact]
        public void Entre_o_54_e_o_canto_vai_bolacha_4_e_39_no_canto()
        {
            // Engenheiro (quarto 1): 54 | 4 | 39 passando no canto — e não 9 + 34.
            var r = NovoModulador().PrimeiraFiada(44, P(encosta: Bordas.Inicio, passa: Bordas.Fim, ajuste: Bordas.Fim));

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("4 39", Seq(r));
        }

        [Fact]
        public void Bolacha_nunca_no_quadrado_do_canto()
        {
            var m = NovoModulador();
            for (int cm = 24; cm <= 404; cm += 5)
            {
                var r = m.PrimeiraFiada(cm, P(encosta: Bordas.Inicio, passa: Bordas.Fim, ajuste: Bordas.Fim));
                if (!r.Sucesso) continue;
                Assert.False(r.Pecas.Last().Peca.EhCompensador, $"{cm}: {Seq(r)}");
            }
        }

        [Fact]
        public void Bolacha_so_encostada_no_encontro_nunca_no_meio()
        {
            var m = NovoModulador();
            for (int cm = 14; cm <= 604; cm += 5)
            {
                var r = m.PrimeiraFiada(cm, P(encosta: Bordas.Ambas));
                if (!r.Sucesso || r.Avisos.Count > 0) continue;
                for (int i = 1; i < r.Pecas.Count - 1; i++)
                    Assert.False(r.Pecas[i].Peca.EhCompensador, $"{cm}: {Seq(r)}");
            }
        }

        [Fact]
        public void Meio_bloco_so_na_ombreira_de_porta()
        {
            var m = NovoModulador();
            for (int cm = 19; cm <= 604; cm += 5)
            {
                var r = m.PrimeiraFiada(cm, P(porta: Bordas.Fim, encosta: Bordas.Inicio, ajuste: Bordas.Inicio));
                if (!r.Sucesso) continue;
                for (int i = 0; i < r.Pecas.Count - 1; i++)
                    Assert.NotEqual("19", r.Pecas[i].Peca.Codigo);
            }
        }

        [Fact]
        public void Ombreira_de_porta_alterna_19_e_39_entre_as_fiadas()
        {
            // Do encontro até a porta: numa fiada o trecho tem 20 cm a mais que na outra (o encontro alterna).
            var m = NovoModulador();
            var f1 = m.PrimeiraFiada(339, P(porta: Bordas.Fim, encosta: Bordas.Inicio, ajuste: Bordas.Inicio));
            var f2 = m.PrimeiraFiada(319, P(porta: Bordas.Fim, encosta: Bordas.Inicio, ajuste: Bordas.Inicio));

            Assert.Equal("19", f1.Pecas.Last().Peca.Codigo);
            Assert.Equal("39", f2.Pecas.Last().Peca.Codigo);
            Assert.Equal(8, f2.Pecas.Count(p => p.Peca.Codigo == "39"));
        }

        [Fact]
        public void Boneca_entre_porta_e_encontro_aceita_a_peca_unica()
        {
            var m = NovoModulador();
            Assert.Equal("4", Seq(m.PrimeiraFiada(4, P(porta: Bordas.Fim, encosta: Bordas.Inicio))));
            Assert.Equal("19", Seq(m.PrimeiraFiada(19, P(porta: Bordas.Fim, passa: Bordas.Inicio))));
        }

        [Fact]
        public void Bolacha_evita_encostar_no_bloco_do_canto()
        {
            // Engenheiro (2ª fiada do quarto 1): parede cortada | 9 | 39 | canto — a bolacha fica do lado do corte.
            var r = NovoModulador().SegundaFiada(49, new double[0],
                P(encosta: Bordas.Ambas, ajuste: Bordas.Ambas, canto: Bordas.Fim));

            Assert.Equal("9 39", Seq(r));
        }

        [Fact]
        public void Peca_desabilitada_nunca_e_usada()
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            familia.Buscar("34").Habilitada = false;

            var r = new Modulador(familia).PrimeiraFiada(259, P(encosta: Bordas.Ambas));

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.DoesNotContain(r.Pecas, p => p.Peca.Codigo == "34");
        }

        [Fact]
        public void Nao_fecha_coloca_as_pecas_a_partir_do_encontro_e_deixa_a_sobra_na_porta()
        {
            var r = NovoModulador().PrimeiraFiada(601, P(porta: Bordas.Fim, encosta: Bordas.Inicio));

            Assert.False(r.Sucesso);
            Assert.True(r.Parcial);
            Assert.Equal(0, r.Pecas.First().Inicio, 6);       // encostado no encontro
            Assert.True(r.Sobra > 0);
            Assert.NotEmpty(r.Sugestoes);

            var espelho = NovoModulador().PrimeiraFiada(601, P(porta: Bordas.Inicio, encosta: Bordas.Fim));
            Assert.True(espelho.SobraNoInicio);
            Assert.Equal(601, espelho.Pecas.Last().Fim, 6);
        }

        [Theory]
        [InlineData(599)]
        [InlineData(604)]
        [InlineData(394)]
        [InlineData(1009)]
        public void Segunda_fiada_nao_tem_junta_a_prumo(int comprimento)
        {
            var m = NovoModulador();
            var pontas = P(encosta: Bordas.Ambas, ajuste: Bordas.Inicio);
            var juntas1 = m.PrimeiraFiada(comprimento, pontas).Juntas().ToList();

            var segunda = m.SegundaFiada(comprimento, juntas1, P(encosta: Bordas.Ambas, ajuste: Bordas.Ambas));

            Assert.True(segunda.Sucesso, segunda.Mensagem);
            foreach (var j2 in segunda.Juntas())
                Assert.True(juntas1.Min(j1 => Math.Abs(j1 - j2)) >= 10 - 1e-9, $"junta {j2} a prumo");
        }

        [Fact]
        public void Canto_alternado_deixa_as_juntas_a_meio_bloco()
        {
            // Parede de 395 cm entre eixos: na 1ª fiada passa no início (34 no canto) e encosta no fim; na 2ª
            // fiada encosta no início (começa 15 cm depois) e passa no fim.
            var m = NovoModulador();
            var f1 = m.PrimeiraFiada(394, P(passa: Bordas.Inicio, encosta: Bordas.Fim, ajuste: Bordas.Inicio));
            var juntas1 = f1.Juntas().Select(j => j - 15).ToList();   // no referencial da 2ª fiada

            var f2 = m.SegundaFiada(394, juntas1, P(encosta: Bordas.Inicio, passa: Bordas.Fim, ajuste: Bordas.Ambas));

            Assert.Equal("34 39 39 39 39 39 39 39 39 39", Seq(f1));
            Assert.Equal("39 39 39 39 39 39 39 39 39 34", Seq(f2));
            Assert.All(f2.Juntas(), j => Assert.Equal(20, juntas1.Min(x => Math.Abs(x - j)), 6));
        }

        [Fact]
        public void Custo_para_o_planejador_ignora_a_posicao_e_recusa_o_que_nao_fecha()
        {
            var m = NovoModulador();
            Assert.Null(m.Custo(601, P(encosta: Bordas.Ambas)));
            Assert.Equal(m.Custo(259, P(encosta: Bordas.Ambas, ajuste: Bordas.Inicio)),
                         m.Custo(259, P(encosta: Bordas.Ambas, ajuste: Bordas.Fim)));
        }

        [Fact]
        public void Reordenar_muda_so_a_ordem_das_pecas()
        {
            var m = NovoModulador();
            var pontas = P(encosta: Bordas.Ambas, ajuste: Bordas.Inicio);
            var r = m.PrimeiraFiada(259, pontas);
            var seq = r.Pecas.Select(p => p.Peca).Reverse().ToList();

            var alt = m.Reordenar(r, seq, pontas);

            Assert.Equal("39 39 39 34 34 34 34", Seq(alt));
            Assert.Equal(259, alt.Pecas.Last().Fim, 6);
        }
    }
}
