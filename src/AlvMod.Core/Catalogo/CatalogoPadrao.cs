using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Catalogo
{
    /// <summary>
    /// Catálogo inicial, gravado no arquivo de catálogo na primeira execução. Depois disso o usuário
    /// edita pelo comando ALVCATALOGO (ou direto no JSON).
    /// Nomes de bloco, layers e cores conforme o padrão do escritório (P = planta, Bloco, tamanho):
    /// bloco P-Bloco39 no layer "P-Bloco 14x39", e assim por diante. Só blocos de concreto.
    /// </summary>
    public static class CatalogoPadrao
    {
        public const string Concreto39 = "CONCRETO-39";

        /// <summary>Sobe quando os padrões mudam, para migrar catálogos já gravados (ver <see cref="Migrar"/>).</summary>
        public const int VersaoAtual = 5;

        public static CatalogoBlocos Criar() => new CatalogoBlocos
        {
            VersaoPadrao = VersaoAtual,
            Familias = new List<Familia> { Familia39Concreto() }
        };

        public static Familia Familia39Concreto() => new Familia
        {
            Nome = Concreto39,
            Material = Material.Concreto,
            Junta = 1,
            Pecas = new List<Peca>
            {
                // Custos = ordem de prioridade do motor: 39 primeiro (o mais barato), 34 em segundo, bolachas por último.
                // 34 = 45: até três 34 no meio da parede antes de passar para duas bolachas ("alguns compensadores
                // são melhores que a parede cheia de 34"). O 19 só entra em ponta livre (obrigatório junto à porta);
                // o 54 é peça fixa nos encontros em T e cruz.
                Nova(39, FuncaoPeca.Principal, 10, cor: 74),
                Nova(19, FuncaoPeca.Meio, 60, cor: 204),
                Nova(54, FuncaoPeca.Encontro, 10, cor: 30),
                Nova(34, FuncaoPeca.Amarracao, 45, cor: 170),
                Nova(9, FuncaoPeca.Compensador, 80, cor: 8, grauteavel: false),
                Nova(4, FuncaoPeca.Compensador, 90, cor: 8, grauteavel: false),
            }
        };

        /// <summary>
        /// Custo sugerido ao cadastrar uma peça nova (ALVCATALOGO), na mesma escala do catálogo padrão:
        /// principal 10, encontro 10, 34 de amarração 45, meio bloco 60, compensadores 80 (≥ 9 cm) ou 90.
        /// </summary>
        public static int CustoSugerido(FuncaoPeca funcao, int comprimento)
        {
            switch (funcao)
            {
                case FuncaoPeca.Principal: return 10;
                case FuncaoPeca.Encontro: return 10;
                case FuncaoPeca.Amarracao: return 45;
                case FuncaoPeca.Meio: return 60;
                case FuncaoPeca.Compensador: return comprimento >= 9 ? 80 : 90;
                default: return 0;
            }
        }

        private static Peca Nova(int comprimento, FuncaoPeca funcao, int custo, short cor,
            bool grauteavel = true, bool habilitada = true) => new Peca
        {
            Codigo = comprimento.ToString(),
            Comprimento = comprimento,
            Largura = 14,
            Altura = 19,
            Funcao = funcao,
            NomeBlocoDwg = $"P-Bloco{comprimento}",
            Layer = $"P-Bloco 14x{comprimento}",
            Cor = cor,
            Custo = custo,
            Grauteavel = grauteavel,
            Habilitada = habilitada
        };

        /// <summary>
        /// Atualiza um catálogo gravado por uma versão anterior do plugin: as peças que ainda estão com os
        /// nomes padrão antigos passam para os nomes, layers e cores reais; a família cerâmica padrão
        /// (que não é usada) sai se não foi alterada. Peças editadas pelo usuário não são tocadas.
        /// </summary>
        /// <returns>true se algo mudou e o arquivo deve ser regravado.</returns>
        public static bool Migrar(CatalogoBlocos catalogo)
        {
            if (catalogo.VersaoPadrao >= VersaoAtual) return false;
            if (catalogo.VersaoPadrao < 2) MigrarNomes(catalogo);

            // v3: o 54 deixa de ser desligado; o motor passa a usá-lo só nas pontas em encontro.
            foreach (var p in catalogo.Familias.SelectMany(f => f.Pecas))
                if (p.Funcao == FuncaoPeca.Encontro && p.NomeBlocoDwg == "P-Bloco54") p.Habilitada = true;

            // v4/v5: nova escala de prioridades. Só troca o custo de quem ainda está com um valor padrão antigo.
            var antigos = new Dictionary<string, (int[] antigos, int novo)>
            {
                ["39"] = (new[] { 10 }, 10), ["19"] = (new[] { 12 }, 60), ["54"] = (new[] { 14 }, 10),
                ["34"] = (new[] { 20, 40 }, 45), ["9"] = (new[] { 22 }, 80), ["4"] = (new[] { 24 }, 90)
            };
            foreach (var f in catalogo.Familias)
                foreach (var p in f.Pecas)
                    if (p.Codigo != null && antigos.TryGetValue(p.Codigo, out var c) && c.antigos.Contains(p.Custo))
                        p.Custo = c.novo;

            catalogo.VersaoPadrao = VersaoAtual;
            return true;
        }

        private static void MigrarNomes(CatalogoBlocos catalogo)
        {
            var novo = Familia39Concreto();
            foreach (var f in catalogo.Familias)
            {
                foreach (var p in f.Pecas)
                {
                    var padrao = novo.Buscar(p.Codigo);
                    if (padrao != null && p.NomeBlocoDwg == $"P-{p.Comprimento}X14X19")
                    {
                        p.NomeBlocoDwg = padrao.NomeBlocoDwg;
                        p.Layer = padrao.Layer;
                        p.Cor = padrao.Cor;
                    }
                }
                // Canaletas padrão antigas (nomes supostos) saem; voltam na fase das elevações.
                f.Pecas.RemoveAll(p => p.Funcao == FuncaoPeca.Canaleta && p.NomeBlocoDwg.StartsWith("P-CAN-", StringComparison.Ordinal));
            }
            catalogo.Familias.RemoveAll(f => f.Nome == "CERAMICO-39" &&
                                             f.Pecas.All(p => p.NomeBlocoDwg.StartsWith("P-CER-", StringComparison.Ordinal)));
        }
    }
}
