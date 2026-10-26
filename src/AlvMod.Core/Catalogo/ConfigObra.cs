using System.Collections.Generic;
using System.Runtime.Serialization;

namespace AlvMod.Core.Catalogo
{
    /// <summary>O que há num vão entre dois segmentos de uma parede (comandos parede por parede).</summary>
    public enum TipoAbertura
    {
        Porta,
        Janela,
        /// <summary>Vão sem esquadria (não soma folga de marco).</summary>
        Passagem
    }

    /// <summary>
    /// Configuração da obra, gravada DENTRO do DWG: quem abrir o arquivo em outra máquina usa a mesma.
    /// </summary>
    [DataContract]
    public sealed class ConfigObra
    {
        [DataMember(Name = "familia", Order = 1)] public string Familia { get; set; }

        /// <summary>Códigos das peças que a construtora não usa nesta obra.</summary>
        [DataMember(Name = "pecasDesabilitadas", Order = 2)] public List<string> PecasDesabilitadas { get; set; } = new List<string>();

        /// <summary>Quantos cm vale 1 unidade do desenho.</summary>
        [DataMember(Name = "fatorCm", Order = 3)] public double FatorCm { get; set; } = 1;

        /// <summary>Denominador da escala de impressão (50 para 1:50). Define o tamanho das cotas.</summary>
        [DataMember(Name = "escala", Order = 4)] public double Escala { get; set; } = 50;

        /// <summary>Deslocamento (unidades do desenho) da planta da 1ª fiada até a planta da 2ª fiada.</summary>
        [DataMember(Name = "deslocamento2F", Order = 5)] public double[] Deslocamento2F { get; set; }

        /// <summary>Preço unitário por código de peça, quando a construtora informa.</summary>
        [DataMember(Name = "precos", Order = 6)] public Dictionary<string, double> Precos { get; set; } = new Dictionary<string, double>();

        /// <summary>Folga de marco somada à medida nominal das portas (cm, total dos dois lados): 80 → 91.</summary>
        [DataMember(Name = "folgaPorta", Order = 7)] public double FolgaPorta { get; set; } = 11;

        /// <summary>
        /// Folga somada à medida nominal das janelas (cm, total). Na modulação do engenheiro: 150 → 151.
        /// As janelas não cortam a 1ª e a 2ª fiada; a folga só aparece na cota e na anotação da abertura.
        /// </summary>
        [DataMember(Name = "folgaJanela", Order = 8)] public double FolgaJanela { get; set; } = 1;

        /// <summary>Layers da arquitetura lidos pelo ALVPLANTA (escolhidos clicando numa linha de cada tipo).</summary>
        [DataMember(Name = "layersParede", Order = 9)] public List<string> LayersParede { get; set; } = new List<string>();
        [DataMember(Name = "layersPorta", Order = 10)] public List<string> LayersPorta { get; set; } = new List<string>();
        [DataMember(Name = "layersJanela", Order = 11)] public List<string> LayersJanela { get; set; } = new List<string>();

        /// <summary>
        /// Paredes que não são de alvenaria (drywall, shaft), marcadas clicando: ponto em cm no referencial da
        /// arquitetura. O trecho entre os encontros vizinhos ao ponto não é modulado.
        /// </summary>
        [DataMember(Name = "exclusoes", Order = 12)] public List<Exclusao> Exclusoes { get; set; } = new List<Exclusao>();

        /// <summary>Quanto (cm) uma porta pode andar para os pedaços de parede fecharem com blocos inteiros.</summary>
        [DataMember(Name = "deslocamentoPorta", Order = 13)] public double DeslocamentoMaximoPorta { get; set; } = 12;

        /// <summary>Numeração por tipo (paredes iguais com o mesmo número) em vez de uma por parede.</summary>
        [DataMember(Name = "numeracaoPorTipo", Order = 14)] public bool NumeracaoPorTipo { get; set; } = true;

        /// <summary>Altura das portas para a anotação da abertura ("91x220").</summary>
        [DataMember(Name = "alturaPorta", Order = 15)] public double AlturaPorta { get; set; } = 220;

        /// <summary>
        /// Quadro de janelas: para cada largura desenhada (cm, texto), a anotação "largura x altura / peitoril"
        /// informada pelo projetista (ex.: "150" → "150 x 100 / 120"). A largura informada vale nas cotas.
        /// </summary>
        [DataMember(Name = "janelas", Order = 16)] public Dictionary<string, string> Janelas { get; set; } = new Dictionary<string, string>();

        [OnDeserializing]
        private void AntesDeLer(StreamingContext _)
        {
            LayersParede = new List<string>();
            LayersPorta = new List<string>();
            LayersJanela = new List<string>();
            PecasDesabilitadas = new List<string>();
            FatorCm = 1;
            Escala = 50;
            Precos = new Dictionary<string, double>();
            FolgaPorta = 11;
            FolgaJanela = 1;
            Exclusoes = new List<Exclusao>();
            DeslocamentoMaximoPorta = 12;
            NumeracaoPorTipo = true;
            AlturaPorta = 220;
            Janelas = new Dictionary<string, string>();
        }

        public bool Configurada => !string.IsNullOrEmpty(Familia);
    }

    /// <summary>Parede marcada como não sendo de alvenaria.</summary>
    [DataContract]
    public sealed class Exclusao
    {
        [DataMember(Name = "x", Order = 1)] public double X { get; set; }
        [DataMember(Name = "y", Order = 2)] public double Y { get; set; }

        /// <summary>"drywall" (escreve "PAREDE EM DRY WALL" na planta) ou "shaft".</summary>
        [DataMember(Name = "tipo", Order = 3)] public string Tipo { get; set; } = "drywall";
    }
}
