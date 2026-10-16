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
        public void Pecas_principais_ficam_no_inicio_e_especiais_no_fim()
        {
            var r = NovoModulador().PrimeiraFiada(604);

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("39", r.Pecas.First().Peca.Codigo);
            Assert.NotEqual("39", r.Pecas.Last().Peca.Codigo);
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
            var juntas1 = primeira.Juntas(1).ToList();

            var segunda = modulador.SegundaFiada(comprimento, juntas1);

            Assert.True(segunda.Sucesso, segunda.Mensagem);
            Assert.Equal(comprimento, segunda.Pecas.Last().Fim);
            foreach (var j2 in segunda.Juntas(1))
                Assert.All(juntas1, j1 => Assert.True(Math.Abs(j1 - j2) >= 10, $"defasagem menor que 1/4 de bloco em {j2}"));
        }

        [Fact]
        public void Segunda_fiada_de_599_comeca_com_meio_bloco()
        {
            var modulador = NovoModulador();
            var juntas1 = modulador.PrimeiraFiada(599).Juntas(1);

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
            Assert.Equal("4", r.Pecas.Last().Peca.Codigo);
            Assert.DoesNotContain(r.Pecas, p => p.Peca.Codigo == "34");
        }

        [Theory]
        [InlineData(Bordas.Inicio)]
        [InlineData(Bordas.Fim)]
        [InlineData(Bordas.Ambas)]
        public void Compensador_nunca_encosta_em_abertura(Bordas aberturas)
        {
            foreach (var comprimento in new[] { 604, 609, 394, 99, 1234 })
            {
                var r = NovoModulador().PrimeiraFiada(comprimento, aberturas);

                Assert.True(r.Sucesso, r.Mensagem);
                if (aberturas.HasFlag(Bordas.Inicio)) Assert.False(r.Pecas.First().Peca.EhCompensador, $"{comprimento}: início");
                if (aberturas.HasFlag(Bordas.Fim)) Assert.False(r.Pecas.Last().Peca.EhCompensador, $"{comprimento}: fim");
            }
        }

        [Fact]
        public void Segunda_fiada_busca_defasagem_de_meio_bloco()
        {
            var modulador = NovoModulador();
            var juntas1 = modulador.PrimeiraFiada(1009).Juntas(1).ToList();

            var segunda = modulador.SegundaFiada(1009, juntas1);

            var defasagens = segunda.Juntas(1).Select(j2 => juntas1.Min(j1 => Math.Abs(j1 - j2))).ToList();
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
            new Pontas { SemCompensador = Bordas.Ambas, Canto = Bordas.Ambas },
            new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Fim },
            new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Ambas },
            new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Inicio, Canto = Bordas.Fim },
            new Pontas { SemCompensador = Bordas.Ambas, EncontroT = Bordas.Ambas },
            new Pontas { SemCompensador = Bordas.Ambas, EncontroT = Bordas.Fim, Canto = Bordas.Inicio },
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

                        if (f == FuncaoPeca.Meio || f == FuncaoPeca.Encontro) Assert.True(ini || ultimo, onde + ": 19/54 no meio");
                        if (f == FuncaoPeca.Encontro)
                            Assert.True((ini && pontas.EncontroT.HasFlag(Bordas.Inicio)) || (ultimo && pontas.EncontroT.HasFlag(Bordas.Fim)), onde + ": 54 fora do T");
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
        public void Se_o_19_nao_couber_na_porta_relaxa_e_avisa()
        {
            // 39 cm: um bloco inteiro; o 19 + 19 não cabe junto a uma parede de 39 com a regra de porta só numa ponta? Cabe: 19+1+19.
            // Para forçar o relaxamento, só o 39 está habilitado.
            var familia = CatalogoPadrao.Familia39Concreto();
            foreach (var p in familia.Pecas.Where(p => p.Codigo != "39")) p.Habilitada = false;

            var r = new Modulador(familia).PrimeiraFiada(39, new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Fim });

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("39", r.Pecas.Single().Peca.Codigo);
        }

        [Fact]
        public void Bloco_54_so_na_ponta_do_encontro_em_T()
        {
            var m = NovoModulador();
            // 54 + 39 = 94 cm.
            var comT = m.PrimeiraFiada(94, new Pontas { SemCompensador = Bordas.Ambas, EncontroT = Bordas.Fim });
            var semT = m.PrimeiraFiada(94, new Pontas { SemCompensador = Bordas.Ambas, Canto = Bordas.Fim });

            Assert.True(comT.Sucesso, comT.Mensagem);
            Assert.Equal("54", comT.Pecas.Last().Peca.Codigo);
            Assert.DoesNotContain(semT.Pecas, p => p.Peca.Codigo == "54");
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
        public void Canto_prefere_o_34_na_ponta()
        {
            var r = NovoModulador().PrimeiraFiada(154, new Pontas { SemCompensador = Bordas.Ambas, Canto = Bordas.Fim });

            Assert.True(r.Sucesso, r.Mensagem);
            Assert.Equal("34", r.Pecas.Last().Peca.Codigo);
        }

        [Fact]
        public void Sobra_vai_para_o_lado_da_porta_e_nunca_para_o_lado_do_encontro()
        {
            var m = NovoModulador();
            // 600 cm não fecha (sobra de 1 cm). Encontro no início e porta no fim: sobra no fim.
            var noFim = m.PrimeiraFiada(600, new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Fim });
            // Encontro no fim e porta no início: as peças encostam no encontro e a sobra fica no início.
            var noInicio = m.PrimeiraFiada(600, new Pontas { SemCompensador = Bordas.Ambas, Porta = Bordas.Inicio });

            Assert.True(noFim.Parcial);
            Assert.False(noFim.SobraNoInicio);
            Assert.Equal(0, noFim.Pecas.First().Inicio, 3);

            Assert.True(noInicio.Parcial);
            Assert.True(noInicio.SobraNoInicio);
            Assert.Equal(600 - noInicio.Sobra, noInicio.Pecas.Last().Fim - noInicio.Sobra, 3);
            Assert.Equal(600, noInicio.Pecas.Last().Fim, 3);   // encostada no encontro
            Assert.Equal(noInicio.Sobra, noInicio.Pecas.First().Inicio, 3);
            for (int i = 1; i < noInicio.Pecas.Count; i++)
                Assert.Equal(1, noInicio.Pecas[i].Inicio - noInicio.Pecas[i - 1].Fim, 3);
        }
    }
}
