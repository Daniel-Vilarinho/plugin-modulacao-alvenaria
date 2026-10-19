using System;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using Xunit;

namespace AlvMod.Core.Tests
{
    public class ModuladorTests
    {
        private static Modulador NovoModulador() => new Modulador(CatalogoPadrao.Familia39Concreto());

        [Fact]
        public void Passo_da_familia_39_e_5cm()
        {
            Assert.Equal(5, NovoModulador().Passo);
        }

        [Fact]
        public void Seiscentos_cm_nao_fecha_sugere_599_ou_604_e_preenche_ate_onde_da()
        {
            var r = NovoModulador().PrimeiraFiada(600);

            Assert.False(r.Sucesso);
            Assert.True(r.Parcial);
            Assert.Equal(599, r.Pecas.Last().Fim);
            Assert.Equal(1, r.Sobra, 3);
            Assert.Equal(new[] { 599, 604 }, r.Sugestoes);
            Assert.Contains("599 ou 604", r.Mensagem);
        }

        [Fact]
        public void Seiscentos_e_quarenta_nao_fecha()
        {
            Assert.False(NovoModulador().PrimeiraFiada(640).Sucesso);
        }

        [Fact]
        public void Quinhentos_e_noventa_e_nove_sao_quinze_blocos_de_39()
        {
            var r = NovoModulador().PrimeiraFiada(599);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(15, r.Pecas.Count);
            Assert.All(r.Pecas, p => Assert.Equal("39", p.Peca.Codigo));
            Assert.Equal(599, r.Pecas.Last().Fim);
        }

        [Theory]
        [InlineData(4)]
        [InlineData(9)]
        [InlineData(19)]
        [InlineData(104)]
        [InlineData(604)]
        [InlineData(639)]
        [InlineData(1234)]
        public void Todo_comprimento_terminado_em_4_ou_9_fecha_exatamente(int comprimento)
        {
            var r = NovoModulador().PrimeiraFiada(comprimento);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(comprimento, r.Pecas.Last().Fim);
        }

        [Fact]
        public void Tolerancia_aceita_medida_com_ruido_de_desenho()
        {
            Assert.True(NovoModulador().PrimeiraFiada(599.3).Sucesso);
            Assert.False(NovoModulador().PrimeiraFiada(599.8).Sucesso);
        }

        [Fact]
        public void Pecas_de_ajuste_ficam_no_meio_da_parede_longe_das_pontas()
        {
            var r = NovoModulador().PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("39", r.Pecas.First().Peca.Codigo);
            Assert.Equal("39", r.Pecas.Last().Peca.Codigo);
            var ajuste = r.Pecas.Single(p => p.Peca.Codigo != "39");
            Assert.InRange(ajuste.Inicio, 80, 604 - 80);   // a mais de 80 cm das pontas
        }

        [Fact]
        public void Peca_desabilitada_nunca_e_usada()
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            familia.Buscar("34").Habilitada = false;
            familia.Buscar("9").Habilitada = false;

            var r = new Modulador(familia).PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.DoesNotContain(r.Pecas, p => p.Peca.Codigo == "34" || p.Peca.Codigo == "9");
        }

        [Fact]
        public void Sem_peca_de_ajuste_nao_forca_e_deixa_a_sobra_no_fim()
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            foreach (var p in familia.Pecas.Where(p => p.Codigo != "39")) p.Habilitada = false;

            var r = new Modulador(familia).PrimeiraFiada(604);

            Assert.False(r.Sucesso);
            Assert.True(r.Parcial);
            Assert.All(r.Pecas, p => Assert.Equal("39", p.Peca.Codigo));
            Assert.Equal(5, r.Sobra, 3);
        }

        [Theory]
        [InlineData(599)]
        [InlineData(604)]
        [InlineData(394)]
        [InlineData(1009)]
        public void Segunda_fiada_nao_tem_junta_a_prumo(int comprimento)
        {
            var modulador = NovoModulador();
            var primeira = modulador.PrimeiraFiada(comprimento);
            var juntas1 = primeira.Juntas().ToList();

            var segunda = modulador.SegundaFiada(comprimento, juntas1);

            Assert.True(segunda.Sucesso, segunda.Mensagem);
            Assert.Equal(comprimento, segunda.Pecas.Last().Fim);
            foreach (var j2 in segunda.Juntas())
                Assert.All(juntas1, j1 => Assert.True(Math.Abs(j1 - j2) >= 10, $"defasagem menor que 1/4 de bloco em {j2}"));
        }

        [Fact]
        public void Segunda_fiada_de_599_comeca_com_meio_bloco()
        {
            var modulador = NovoModulador();
            var juntas1 = modulador.PrimeiraFiada(599).Juntas();

            var segunda = modulador.SegundaFiada(599, juntas1);

            Assert.Equal("19", segunda.Pecas.First().Peca.Codigo);
            Assert.Equal("19", segunda.Pecas.Last().Peca.Codigo);
        }

        [Fact]
        public void Segunda_fiada_impossivel_sem_prumo_fecha_com_aviso_em_vez_de_deixar_a_parede_vazia()
        {
            // Juntas da 1ª fiada a cada 5 cm: qualquer 2ª fiada teria junta a prumo.
            var juntas = Enumerable.Range(1, 100).Select(i => i * 5 - 0.5);

            var r = NovoModulador().SegundaFiada(599, juntas);

            Assert.True(r.Sucesso);
            Assert.Contains(r.Avisos, a => a.Contains("prumo"));
        }
    
        [Fact]
        public void Seiscentos_e_quatro_prefere_um_compensador_a_bloco_de_34_mais_compensador()
        {
            var r = NovoModulador().PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(15, r.Pecas.Count(p => p.Peca.Codigo == "39"));
            Assert.Equal(1, r.Pecas.Count(p => p.Peca.Codigo == "4"));
            Assert.DoesNotContain(r.Pecas, p => p.Peca.Codigo == "34");
        }

        [Theory]
        [InlineData(Bordas.Inicio)]
        [InlineData(Bordas.Fim)]
        [InlineData(Bordas.Ambas)]
        public void Compensador_nunca_encosta_em_porta(Bordas aberturas)
        {
            foreach (var comprimento in new[] { 604, 609, 394, 99, 1234 })
            {
                var r = NovoModulador().PrimeiraFiada(comprimento, new Pontas { SemCompensador = aberturas, Porta = aberturas });

                Assert.True(r.Sucesso, r.Mensagem);
                if (aberturas.HasFlag(Bordas.Inicio)) Assert.False(r.Pecas.First().Peca.EhCompensador, $"{comprimento}: início");
                if (aberturas.HasFlag(Bordas.Fim)) Assert.False(r.Pecas.Last().Peca.EhCompensador, $"{comprimento}: fim");
            }
        }

        [Fact]
        public void Segunda_fiada_busca_defasagem_de_meio_bloco()
        {
            var modulador = NovoModulador();
            var juntas1 = modulador.PrimeiraFiada(1009).Juntas().ToList();

            var segunda = modulador.SegundaFiada(1009, juntas1);

            var defasagens = segunda.Juntas().Select(j2 => juntas1.Min(j1 => Math.Abs(j1 - j2))).ToList();
            Assert.True(defasagens.Count(d => d >= 20) >= defasagens.Count - 1, string.Join(",", defasagens));
        }

        [Fact]
        public void Pecas_desabilitadas_na_obra_nao_sao_usadas_e_catalogo_original_fica_intacto()
        {
            var familia = CatalogoPadrao.Familia39Concreto();
            var obra = familia.ParaObra(new[] { "4", "9" });

            var r = new Modulador(obra).PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.DoesNotContain(r.Pecas, p => p.Peca.EhCompensador);
            Assert.True(familia.Buscar("4").Habilitada);
        }
    
        // ------------------------------------------------------------------ regras do engenheiro

        private static readonly Pontas[] Variacoes =
        {
            Pontas.Nenhuma,
            // canto: passa numa ponta, encosta na outra
            new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Ambas, Passa34 = Bordas.Inicio, Exige39 = Bordas.Fim },
            // porta numa ponta, encontro na outra
            new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Fim, Encontro = Bordas.Inicio, Exige39 = Bordas.Inicio },
            new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Ambas },
            // entre dois encontros (corte em volta do T / do 54)
            new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Ambas, Exige39 = Bordas.Ambas },
            new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Ambas, Passa34 = Bordas.Ambas },
        };

        [Fact]
        public void Regras_de_posicao_valem_para_todos_os_comprimentos_e_pontas()
        {
            var m = NovoModulador();
            foreach (var pontas in Variacoes)
                for (int cm = 14; cm <= 800; cm += 5)
                {
                    var r = m.PrimeiraFiada(cm - 1, pontas); // cm-1 termina em 4 ou 9
                    var ps = r.Pecas;
                    for (int i = 0; i < ps.Count; i++)
                    {
                        bool ini = i == 0, ultimo = i == ps.Count - 1;
                        var f = ps[i].Peca.Funcao;
                        string onde = $"{cm - 1} cm, peça {i} ({ps[i].Peca.Codigo})";

                        Assert.NotEqual(FuncaoPeca.Encontro, f);               // o 54 só como peça fixa
                        if (f == FuncaoPeca.Meio)
                        {
                            Assert.True(ini || ultimo, onde + ": 19 no meio");
                            Assert.False(ini && pontas.Encontro.HasFlag(Bordas.Inicio), onde + ": 19 em encontro");
                            Assert.False(ultimo && pontas.Encontro.HasFlag(Bordas.Fim), onde + ": 19 em encontro");
                        }
                        if (f == FuncaoPeca.Compensador)
                        {
                            Assert.False(ini && pontas.SemCompensador.HasFlag(Bordas.Inicio), onde + ": bolacha na ponta");
                            Assert.False(ultimo && pontas.SemCompensador.HasFlag(Bordas.Fim), onde + ": bolacha na ponta");
                        }
                        if (i > 0) Assert.Equal(1, ps[i].Inicio - ps[i - 1].Fim, 3); // junta sempre de 1 cm
                    }
                }
        }

        [Fact]
        public void Ponta_que_encosta_em_porta_termina_em_19()
        {
            var m = NovoModulador();
            foreach (int cm in new[] { 99, 139, 179, 219, 259, 599 })
            {
                var r = m.PrimeiraFiada(cm, new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Fim });
                Assert.True(r.Sucesso, r.Mensagem);
                Assert.Equal("19", r.Pecas.Last().Peca.Codigo);
                Assert.Empty(r.Avisos);

                var espelho = m.PrimeiraFiada(cm, new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Inicio });
                Assert.Equal("19", espelho.Pecas.First().Peca.Codigo);
            }
        }

        [Fact]
        public void Porta_nas_duas_pontas_exige_19_nas_duas()
        {
            var r = NovoModulador().PrimeiraFiada(599, new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Ambas });

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("19", r.Pecas.First().Peca.Codigo);
            Assert.Equal("19", r.Pecas.Last().Peca.Codigo);
            Assert.All(r.Pecas.Skip(1).Take(r.Pecas.Count - 2), p => Assert.Equal("39", p.Peca.Codigo));
        }

        [Fact]
        public void Nunca_19_em_encontro_mesmo_que_ficasse_mais_barato()
        {
            // 99 cm = 39 + 39 + 19 seria o natural; com encontro nas duas pontas o 19 não pode entrar.
            var r = NovoModulador().PrimeiraFiada(99, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Ambas });

            Assert.DoesNotContain(r.Pecas, p => p.Peca.Codigo == "19");
        }

        [Fact]
        public void Canto_parede_que_passa_termina_em_34_e_a_que_encosta_comeca_com_39()
        {
            var m = NovoModulador();
            // Passa no início: 34 + 39×k. 34 + 1 + 39 + 1 + 39 = 114.
            var passa = m.PrimeiraFiada(114, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Inicio, Passa34 = Bordas.Inicio });
            var encosta = m.PrimeiraFiada(119, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Inicio, Exige39 = Bordas.Inicio });

            Assert.True(passa.Sucesso, passa.Mensagem);
            Assert.Equal("34", passa.Pecas.First().Peca.Codigo);
            Assert.True(encosta.Sucesso, encosta.Mensagem);
            Assert.Equal("39", encosta.Pecas.First().Peca.Codigo);
        }

        [Fact]
        public void Motor_nunca_escolhe_o_54_ele_e_peca_fixa_do_encontro()
        {
            var m = NovoModulador();
            for (int cm = 54; cm <= 400; cm += 5)
                Assert.DoesNotContain(m.PrimeiraFiada(cm, Pontas.Nenhuma).Pecas, p => p.Peca.Codigo == "54");
        }

        [Fact]
        public void Priorizar_39_e_usar_34_so_em_segundo_lugar()
        {
            var m = NovoModulador();

            Assert.All(m.PrimeiraFiada(599).Pecas, p => Assert.Equal("39", p.Peca.Codigo));

            // 154 cm = 39 + 39 + 39 + 34: o 34 é a adaptação, e não bolachas.
            var r = m.PrimeiraFiada(154);
            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal(3, r.Pecas.Count(p => p.Peca.Codigo == "39"));
            Assert.Equal(1, r.Pecas.Count(p => p.Peca.Codigo == "34"));
            Assert.DoesNotContain(r.Pecas, p => p.Peca.EhCompensador);
        }

        [Fact]
        public void Diferenca_de_desenho_ate_meio_cm_nao_vira_junta_de_1_5_cm()
        {
            var m = NovoModulador();
            // Entre duas paredes (as duas pontas duras): 599,5 cm em vez de 599 → a diferença é dividida.
            var duro = m.PrimeiraFiada(599.5, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Ambas });
            Assert.True(duro.Sucesso, duro.Mensagem);
            Assert.InRange(duro.Pecas.First().Inicio, 0, 0.05);
            Assert.InRange(599.5 - duro.Pecas.Last().Fim, 0, 0.05);
            for (int i = 1; i < duro.Pecas.Count; i++)
                Assert.InRange(duro.Pecas[i].Inicio - duro.Pecas[i - 1].Fim, 1, 1.05);

            // Com porta no fim: as peças encostam no encontro e a diferença fica do lado da porta.
            var porta = m.PrimeiraFiada(599.5, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Inicio, Porta = Bordas.Fim });
            Assert.Equal(0, porta.Pecas.First().Inicio, 3);
            for (int i = 1; i < porta.Pecas.Count; i++)
                Assert.Equal(1, porta.Pecas[i].Inicio - porta.Pecas[i - 1].Fim, 3);
        }

        [Fact]
        public void Sobra_vai_para_o_lado_da_porta_e_nunca_para_o_lado_do_encontro()
        {
            var m = NovoModulador();
            // 600 cm não fecha. Encontro no início e porta no fim: sobra no fim.
            var noFim = m.PrimeiraFiada(600, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Inicio, Porta = Bordas.Fim });
            // Encontro no fim e porta no início: as peças encostam no encontro e a sobra fica no início.
            var noInicio = m.PrimeiraFiada(600, new Pontas { SemCompensador = Bordas.Ambas, Encontro = Bordas.Fim, Porta = Bordas.Inicio });

            Assert.True(noFim.Parcial);
            Assert.False(noFim.SobraNoInicio);
            Assert.Equal(0, noFim.Pecas.First().Inicio, 3);

            Assert.True(noInicio.Parcial);
            Assert.True(noInicio.SobraNoInicio);
            Assert.Equal(600, noInicio.Pecas.Last().Fim, 3);   // encostada no encontro
            Assert.Equal(noInicio.Sobra, noInicio.Pecas.First().Inicio, 3);
            for (int i = 1; i < noInicio.Pecas.Count; i++)
                Assert.Equal(1, noInicio.Pecas[i].Inicio - noInicio.Pecas[i - 1].Fim, 3);
        }
    }
}
