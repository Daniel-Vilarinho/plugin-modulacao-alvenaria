using System;
using System.Collections.Generic;
using System.Linq;

namespace AlvMod.Core.Planta
{
    /// <summary>Segmento de reta lido do desenho, já convertido para cm.</summary>
    public sealed class Segmento
    {
        public string Layer { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        /// <summary>Quando o segmento é corda de um arco: raio e centro do arco (cm). 0 = reta.</summary>
        public double Raio { get; set; }
        public double Cx { get; set; }
        public double Cy { get; set; }

        public Segmento() { }

        public Segmento(string layer, double x1, double y1, double x2, double y2)
        {
            Layer = layer; X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
        }

        public double Comprimento => Math.Sqrt((X2 - X1) * (X2 - X1) + (Y2 - Y1) * (Y2 - Y1));
    }

    /// <summary>Texto da planta (ex.: "P1", "J3"), em cm. Rótulos P*/J* ajudam a classificar os vãos.</summary>
    public sealed class Rotulo
    {
        public string Texto { get; set; }
        public double X { get; set; }
        public double Y { get; set; }

        public Rotulo() { }
        public Rotulo(string texto, double x, double y) { Texto = texto; X = x; Y = y; }
    }

    public enum Orientacao
    {
        Horizontal,
        Vertical
    }

    public enum TipoVao
    {
        Porta,
        Janela,
        Passagem
    }

    /// <summary>Vão na parede, em cm ao longo do eixo (coordenadas absolutas do desenho).</summary>
    public sealed class Vao
    {
        public TipoVao Tipo { get; set; }

        /// <summary>Bordas da abertura desenhada pelo arquiteto na parede (inclui o marco).</summary>
        public double Inicio { get; set; }
        public double Fim { get; set; }

        /// <summary>
        /// Tamanho da esquadria lido do desenho (folha da porta pelo raio do arco, vidro da janela), em cm.
        /// Só informativo (relatório): a abertura usa a medida nominal do vão desenhado. 0 = não encontrado.
        /// </summary>
        public double Tamanho { get; set; }

        public double Largura => Fim - Inicio;
        public double Centro => (Inicio + Fim) / 2;
        public bool TamanhoLido => Tamanho > 0;

        /// <summary>
        /// Quanto o planejador deslocou a abertura (cm, ao longo do eixo) para os pedaços de parede dos dois
        /// lados fecharem com blocos inteiros (o engenheiro faz isso: a porta anda alguns centímetros).
        /// </summary>
        public double Deslocamento { get; set; }

        /// <summary>
        /// Lado da dobradiça da porta, pelo centro do arco: −1 = lado do início do vão, +1 = lado do fim,
        /// 0 = não lido. Do lado da dobradiça o engenheiro sempre deixa alvenaria (o marco precisa dela).
        /// </summary>
        public int LadoDobradica { get; set; }

        /// <summary>
        /// Medida real informada pelo projetista (quadro de esquadrias), quando o desenho não bate com ela
        /// (ex.: janela desenhada com 50 que na obra é de 60). Nula = vale o desenho.
        /// </summary>
        public double? MedidaInformada { get; set; }

        /// <summary>Medida nominal da esquadria: a informada, ou o vão desenhado arredondado de 5 em 5 cm (79 → 80).</summary>
        public double Nominal => MedidaInformada ?? Math.Round(Largura / 5) * 5;

        /// <summary>
        /// Abertura na alvenaria = medida nominal + folga de marco (porta +11, janela +1: 80 → 91, 150 → 151),
        /// centrada no vão desenhado e deslocada de <see cref="Deslocamento"/>.
        /// </summary>
        public (double a, double b) Alvenaria(double folga)
        {
            double t = Nominal + folga, c = Centro + Deslocamento;
            return (c - t / 2, c + t / 2);
        }
    }

    /// <summary>
    /// Parede reta ortogonal, representada pelo eixo. Horizontal: y = Coord, x de Inicio a Fim.
    /// Vertical: x = Coord, y de Inicio a Fim. Sempre Inicio &lt; Fim (esquerda→direita, baixo→cima).
    /// </summary>
    public sealed class Parede
    {
        public int Indice { get; set; }
        public Orientacao Orientacao { get; set; }
        public double Coord { get; set; }
        public double Inicio { get; set; }
        public double Fim { get; set; }

        /// <summary>Espessura desenhada pelo arquiteto (com acabamento), em cm.</summary>
        public double Espessura { get; set; }

        public List<Vao> Vaos { get; set; } = new List<Vao>();

        /// <summary>Nome da numeração (ex.: "PAREDE 3").</summary>
        public string Nome { get; set; }

        public double Comprimento => Fim - Inicio;
        public bool Horizontal => Orientacao == Orientacao.Horizontal;

        public (double X, double Y) Ponto(double t) => Horizontal ? (t, Coord) : (Coord, t);

        /// <summary>Coordenada ao longo do eixo de um ponto qualquer.</summary>
        public double AoLongo(double x, double y) => Horizontal ? x : y;

        /// <summary>Coordenada transversal (perpendicular ao eixo) de um ponto qualquer.</summary>
        public double Transversal(double x, double y) => Horizontal ? y : x;

        /// <summary>Vãos que interrompem a alvenaria na 1ª/2ª fiada (portas e passagens; janelas não).</summary>
        public IEnumerable<Vao> VaosQueCortam() =>
            Vaos.Where(v => v.Tipo != TipoVao.Janela).OrderBy(v => v.Inicio);

        public override string ToString() =>
            $"{Nome ?? "#" + Indice} {(Horizontal ? "H" : "V")} c={Coord:0.#} [{Inicio:0.#}; {Fim:0.#}] e={Espessura:0.#}";
    }

    public enum TipoEncontro
    {
        L,
        T,
        X,
        /// <summary>Paredes colineares que se tocam pela ponta, ou caso não reconhecido.</summary>
        Outro
    }

    /// <summary>Encontro entre paredes, no cruzamento dos eixos.</summary>
    public sealed class Encontro
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Paredes que terminam neste encontro.</summary>
        public List<Parede> Terminais { get; } = new List<Parede>();

        /// <summary>Paredes que continuam através do encontro.</summary>
        public List<Parede> Passantes { get; } = new List<Parede>();

        public TipoEncontro Tipo { get; set; }

        public IEnumerable<Parede> Paredes => Passantes.Concat(Terminais);

        /// <summary>
        /// Parede que passa direto na fiada (amarração direta): na fiada ímpar a principal
        /// (passante; senão a maior — questionário, item 17), na fiada par a seguinte, alternando.
        /// </summary>
        public Parede Dominante(int fiada)
        {
            var ordem = Passantes.OrderByDescending(p => p.Comprimento)
                .Concat(Terminais.OrderByDescending(p => p.Comprimento))
                .ToList();
            if (ordem.Count == 0) return null;
            if (fiada % 2 == 1 || ordem.Count == 1) return ordem[0];
            // Na fiada par passa uma parede perpendicular à dominante da ímpar.
            var primeira = ordem[0];
            return ordem.Skip(1).FirstOrDefault(p => p.Orientacao != primeira.Orientacao) ?? ordem[1];
        }

        public override string ToString() => $"{Tipo} ({X:0.#}, {Y:0.#})";
    }

    public sealed class PlantaDetectada
    {
        public List<Parede> Paredes { get; } = new List<Parede>();
        public List<Encontro> Encontros { get; } = new List<Encontro>();
        public List<string> Avisos { get; } = new List<string>();

        /// <summary>O que foi lido nos layers de parede mas descartado (escada, pedaços de marco...), e por quê.</summary>
        public List<string> Ignoradas { get; } = new List<string>();

        /// <summary>Trechos retirados por serem drywall/shaft (orientação, eixo, início, fim), em cm.</summary>
        public List<(Orientacao orientacao, double coord, double inicio, double fim)> Excluidas { get; } =
            new List<(Orientacao, double, double, double)>();

        /// <summary>Espessura mais frequente das paredes (cm).</summary>
        public double EspessuraTipica { get; set; }

        /// <summary>
        /// Grade modular dos eixos (ver GradeModular): passo e a posição de referência dos eixos verticais (x) e
        /// horizontais (y). Nulo enquanto a grade não foi aplicada.
        /// </summary>
        public (double modulo, double refX, double refY)? Grade { get; set; }

        public IEnumerable<Encontro> EncontrosDe(Parede p) => Encontros.Where(e => e.Paredes.Contains(p));
    }

    public sealed class OpcoesDeteccao
    {
        public HashSet<string> LayersParede { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LayersPorta { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> LayersJanela { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Faixa de espessura (cm) entre duas faces paralelas para serem consideradas parede. Paredes de
        /// bloco 14 ficam com 14 a 20 cm de espessura desenhada; abaixo de 10 cm é linha de acabamento,
        /// acima de 25 cm é degrau de escada ou parede dupla.
        /// </summary>
        public double EspessuraMinima { get; set; } = 10;
        public double EspessuraMaxima { get; set; } = 25;

        /// <summary>Diferença máxima (cm) entre a espessura de uma parede e a espessura típica do desenho.</summary>
        public double VariacaoEspessura { get; set; } = 6;

        /// <summary>Maior vão de janela aceito (cm); acima disso o buraco separa paredes.</summary>
        public double JanelaMaxima { get; set; } = 600;

        /// <summary>Maior porta aceita (cm); um "vão de porta" maior que isso é pareamento errado.</summary>
        public double PortaMaxima { get; set; } = 250;

        /// <summary>Distância máxima (cm) de um rótulo P*/J* ao centro do vão para decidir o tipo dele.</summary>
        public double DistanciaRotulo { get; set; } = 120;

        /// <summary>Paredes mais curtas que isso são ignoradas (cm).</summary>
        public double ComprimentoMinimo { get; set; } = 20;

        /// <summary>Maior vão sem porta/janela que ainda é tratado como passagem na mesma parede (cm).</summary>
        public double PassagemMaxima { get; set; } = 120;

        /// <summary>
        /// Pontos (cm) clicados em paredes que NÃO são de alvenaria (drywall, shaft): o trecho entre os
        /// encontros vizinhos ao ponto não é modulado.
        /// </summary>
        public List<(double x, double y)> Exclusoes { get; set; } = new List<(double x, double y)>();

        /// <summary>Tolerância geral de desenho (cm).</summary>
        public double Tolerancia { get; set; } = 1;
    }
}
