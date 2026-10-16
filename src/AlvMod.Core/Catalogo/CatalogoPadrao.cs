using System.Collections.Generic;

namespace AlvMod.Core.Catalogo
{
    /// <summary>
    /// Catálogo fixo enquanto não existe o banco (fase 1).
    /// Nomes de bloco e layer seguem o arquivo de referência (J Soares) usado no protótipo v0.1.
    /// Custos são PROVISÓRIOS — ver docs/ANALISE.md, itens 7 e 8.
    /// </summary>
    public static class CatalogoPadrao
    {
        public static Familia Familia39Concreto()
        {
            return new Familia
            {
                Nome = "39",
                Material = Material.Concreto,
                Junta = 1,
                Modulo = 40,
                Pecas = new List<Peca>
                {
                    Nova("39", 39, FuncaoPeca.Principal, "P-Bloco39", "BLOCO 14X39", 10),
                    Nova("19", 19, FuncaoPeca.Meio, "P-Bloco 19", "BLOCO 14X19", 12),
                    Nova("34", 34, FuncaoPeca.Amarracao, "P-Bloco 34", "BLOCO 14X34", 14),
                    // 54 é peça de encontro: fica desabilitada em parede isolada até a fase 5.
                    Nova("54", 54, FuncaoPeca.Encontro, "P-Bloco 54", "BLOCO 14X54", 16, habilitada: false),
                    Nova("9", 9, FuncaoPeca.Compensador, "Bloco 9x14", "BOLACHA 14X9", 30, grauteavel: false),
                    Nova("4", 4, FuncaoPeca.Compensador, "P-Bolacha 4x14", "BOLACHA 14X4", 40, grauteavel: false),
                    Nova("C39", 39, FuncaoPeca.Canaleta, "Canaleta_39x14", "CANALETA 14X39", 0),
                    Nova("C34", 34, FuncaoPeca.Canaleta, "Canaleta_34x14", "CANALETA 14X34", 0),
                    Nova("C19", 19, FuncaoPeca.Canaleta, "Canaleta_19x14", "CANALETA 14X19", 0),
                }
            };
        }

        private static Peca Nova(string codigo, int comprimento, FuncaoPeca funcao, string bloco, string layer,
            int custo, bool grauteavel = true, bool habilitada = true)
        {
            return new Peca
            {
                Codigo = codigo,
                Comprimento = comprimento,
                Largura = 14,
                Funcao = funcao,
                NomeBlocoDwg = bloco,
                Layer = layer,
                Custo = custo,
                Grauteavel = grauteavel,
                Habilitada = habilitada
            };
        }
    }
}
