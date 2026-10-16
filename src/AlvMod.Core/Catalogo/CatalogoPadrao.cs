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
        public const int VersaoAtual = 3;

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
                Nova(39, FuncaoPeca.Principal, 10, cor: 74),
                Nova(19, FuncaoPeca.Meio, 12, cor: 204),
                Nova(54, FuncaoPeca.Encontro, 14, cor: 30), // o motor só usa na ponta que chega num encontro
                Nova(34, FuncaoPeca.Amarracao, 20, cor: 170),
                Nova(9, FuncaoPeca.Compensador, 22, cor: 8, grauteavel: false),
                Nova(4, FuncaoPeca.Compensador, 24, cor: 8, grauteavel: false),
            }
        };

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
