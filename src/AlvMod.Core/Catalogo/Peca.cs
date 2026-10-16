using System;
using System.Runtime.Serialization;

namespace AlvMod.Core.Catalogo
{
    public enum FuncaoPeca
    {
        Principal,
        Amarracao,
        Meio,
        Encontro,
        Compensador,
        Canaleta
    }

    /// <summary>Peça (bloco de alvenaria) de uma família. Medidas em cm.</summary>
    [DataContract]
    public sealed class Peca
    {
        [DataMember(Name = "codigo", Order = 1)] public string Codigo { get; set; }
        [DataMember(Name = "comprimento", Order = 2)] public int Comprimento { get; set; }
        [DataMember(Name = "largura", Order = 3)] public int Largura { get; set; } = 14;
        [DataMember(Name = "altura", Order = 4)] public int Altura { get; set; } = 19;

        public FuncaoPeca Funcao { get; set; }

        /// <summary>Função gravada como texto no JSON, para o arquivo poder ser editado à mão.</summary>
        [DataMember(Name = "funcao", Order = 5)]
        private string FuncaoTexto
        {
            get => Funcao.ToString();
            set => Funcao = Enum.TryParse(value, true, out FuncaoPeca f) ? f : FuncaoPeca.Principal;
        }

        /// <summary>Nome do bloco DWG (BlockReference) da peça em planta.</summary>
        [DataMember(Name = "blocoPlanta", Order = 6)] public string NomeBlocoDwg { get; set; }

        [DataMember(Name = "layer", Order = 7)] public string Layer { get; set; }

        /// <summary>Cor do layer (índice de cor do AutoCAD, 1–255) usada quando o layer é criado.</summary>
        [DataMember(Name = "cor", Order = 8)] public short Cor { get; set; } = 7;

        [DataMember(Name = "grauteavel", Order = 9)] public bool Grauteavel { get; set; } = true;

        /// <summary>
        /// Custo relativo usado pelo motor para escolher entre composições que fecham (menor = preferida).
        /// Questionário: máximo de blocos de 39; poucos compensadores, mas preferíveis a muitos blocos de 34.
        /// </summary>
        [DataMember(Name = "custo", Order = 10)] public int Custo { get; set; }

        /// <summary>Peças desabilitadas no catálogo nunca são usadas (também dá para desabilitar só na obra).</summary>
        [DataMember(Name = "habilitada", Order = 11)] public bool Habilitada { get; set; } = true;

        // O serializador não chama o construtor: repõe os padrões de campos ausentes no JSON.
        [OnDeserializing]
        private void AntesDeLer(StreamingContext _)
        {
            Largura = 14;
            Altura = 19;
            Cor = 7;
            Grauteavel = true;
            Habilitada = true;
        }

        public bool EhCompensador => Funcao == FuncaoPeca.Compensador;

        public string Dimensoes => $"{Comprimento}x{Largura}x{Altura}";

        public Peca Clonar() => (Peca)MemberwiseClone();

        public override string ToString() => Codigo;
    }
}
