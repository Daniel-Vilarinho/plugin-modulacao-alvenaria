using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Planta;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>
    /// Anotações da planta de modulação, no padrão do arquivo do engenheiro: pontos de graute,
    /// nome das paredes, aberturas ("ABERTURA" + "91x220" / "151 x 100 / 120"), "PAREDE EM DRY WALL",
    /// cotas internas em cm e título da planta. Tamanhos de texto em mm no papel.
    /// </summary>
    internal sealed class Anotador
    {
        public const string LayerRotulos = "TX PAREDES";
        public const string LayerAberturas = "ABERTURA";
        public const string LayerCotas = "FO-Cotas";
        public const string LayerTitulos = "FO-Títulos";
        public const string LayerGraute = "Ponto de GROUT - PLANTA";
        public const string BlocoGraute = "PONTO DE GROUT PLANTA";

        /// <summary>Todos os layers que o plugin desenha (para apagar uma modulação anterior).</summary>
        public static readonly string[] Layers = { LayerRotulos, LayerAberturas, LayerCotas, LayerTitulos, LayerGraute };

        private const double LadoGraute = 8.4; // cm, como o bloco do engenheiro

        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly BlockTableRecord _espaco;
        private readonly double _fator, _escala;
        private readonly Vector3d _deslocamento;
        private readonly ObjectId _estiloTexto;
        private ObjectId _blocoGraute;
        private double _escalaGraute = 1;
        private Point3d _centroGraute;

        public int Grautes { get; private set; }
        public int Cotas { get; private set; }

        public Anotador(Database db, Transaction tr, double fator, double escala, Vector3d deslocamento = default)
        {
            _db = db;
            _tr = tr;
            _fator = fator;
            _escala = escala;
            _deslocamento = deslocamento;
            _espaco = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
            _estiloTexto = Camadas.Texto(db, tr);
        }

        /// <summary>Milímetros no papel → unidades do desenho.</summary>
        private double Mm(double mm) => mm * _escala / (10.0 * _fator);

        private Point3d P(double xCm, double yCm) => new Point3d(xCm / _fator, yCm / _fator, 0) + _deslocamento;

        private (double x, double y) NaParede(Parede w, double aoLongo, double transversal) =>
            w.Horizontal ? (aoLongo, w.Coord + transversal) : (w.Coord + transversal, aoLongo);

        private void Adicionar(Entity e)
        {
            _espaco.AppendEntity(e);
            _tr.AddNewlyCreatedDBObject(e, true);
        }

        private void Texto(string texto, double xCm, double yCm, double alturaMm, double rotacao, string layer,
            TextHorizontalMode h = TextHorizontalMode.TextCenter, TextVerticalMode v = TextVerticalMode.TextVerticalMid)
        {
            var p = P(xCm, yCm);
            var t = new DBText
            {
                TextString = texto,
                Height = Mm(alturaMm),
                Rotation = rotacao,
                TextStyleId = _estiloTexto,
                Layer = layer,
                HorizontalMode = h,
                VerticalMode = v,
                Position = p,
                AlignmentPoint = p
            };
            Adicionar(t);
            t.AdjustAlignment(_db);
        }

        // ------------------------------------------------------------------ graute

        /// <summary>Ponto de graute (quadrado cheio) no centro do encontro.</summary>
        public void Graute(double xCm, double yCm)
        {
            string layer = Camadas.Garantir(_db, _tr, LayerGraute, 1);
            if (_blocoGraute.IsNull) PrepararBlocoGraute();
            var centro = P(xCm, yCm);
            var br = new BlockReference(centro - (_centroGraute - Point3d.Origin) * _escalaGraute, _blocoGraute)
            {
                ScaleFactors = new Scale3d(_escalaGraute),
                Layer = layer
            };
            Adicionar(br);
            Grautes++;
        }

        /// <summary>Usa o bloco do escritório se existir no desenho (no tamanho dele), senão cria um quadrado cheio.</summary>
        private void PrepararBlocoGraute()
        {
            var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            if (bt.Has(BlocoGraute))
            {
                _blocoGraute = bt[BlocoGraute];
                var ext = Extensao(_blocoGraute);
                if (ext != null)
                {
                    var e = ext.Value;
                    double lado = Math.Max(e.MaxPoint.X - e.MinPoint.X, e.MaxPoint.Y - e.MinPoint.Y);
                    _centroGraute = new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2, 0);
                    if (lado > 0) _escalaGraute = LadoGraute / _fator / lado;
                }
                return;
            }
            bt.UpgradeOpen();
            var btr = new BlockTableRecord { Name = BlocoGraute, Origin = Point3d.Origin };
            _blocoGraute = bt.Add(btr);
            _tr.AddNewlyCreatedDBObject(btr, true);
            double l = LadoGraute / _fator;
            var quadrado = new Polyline(2) { Layer = "0", ConstantWidth = l };   // polilinha larga = quadrado cheio
            quadrado.AddVertexAt(0, new Point2d(-l / 2, 0), 0, l, l);
            quadrado.AddVertexAt(1, new Point2d(l / 2, 0), 0, l, l);
            btr.AppendEntity(quadrado);
            _tr.AddNewlyCreatedDBObject(quadrado, true);
            _centroGraute = Point3d.Origin;
            _escalaGraute = 1;
        }

        private Extents3d? Extensao(ObjectId bloco)
        {
            Extents3d? ext = null;
            var btr = (BlockTableRecord)_tr.GetObject(bloco, OpenMode.ForRead);
            foreach (ObjectId filho in btr)
            {
                if (!(_tr.GetObject(filho, OpenMode.ForRead) is Entity e) || e is AttributeDefinition) continue;
                try
                {
                    var x = e.GeometricExtents;
                    if (ext == null) ext = x;
                    else { var t = ext.Value; t.AddExtents(x); ext = t; }
                }
                catch (Autodesk.AutoCAD.Runtime.Exception) { /* entidade sem extensão */ }
            }
            return ext;
        }

        // ------------------------------------------------------------------ nomes, aberturas, drywall

        /// <summary>Nome da parede ("PAREDE 6") no meio dela, do lado de dentro, a 37 cm do eixo.</summary>
        public void Rotulo(PlantaDetectada planta, Parede w)
        {
            string layer = Camadas.Garantir(_db, _tr, LayerRotulos, 1);
            int lado = Anotacoes.LadoDoRotulo(planta, w);
            var (x, y) = NaParede(w, (w.Inicio + w.Fim) / 2, lado * 37);
            Texto(w.Nome, x, y, 3.3, w.Horizontal ? 0 : Math.PI / 2, layer);
        }

        /// <summary>
        /// Abertura: retângulo do vão na alvenaria, "ABERTURA" de um lado e a medida do outro
        /// (porta "91x220", janela "151 x 100 / 120" quando o quadro de janelas foi informado).
        /// </summary>
        public void Abertura(PlantaDetectada planta, Parede w, Vao v, OpcoesFiadas op, ConfigObra cfg)
        {
            if (v.Tipo == TipoVao.Passagem) return;
            string layer = Camadas.Garantir(_db, _tr, LayerAberturas, 7);
            bool porta = v.Tipo == TipoVao.Porta;
            var (a, b) = v.Alvenaria(porta ? op.FolgaPorta : op.FolgaJanela);
            double meia = porta ? op.Largura / 2 : 15.8;
            var ret = new Polyline(4) { Closed = true, Layer = layer };
            int i = 0;
            foreach (var (t, d) in new[] { (a, -meia), (b, -meia), (b, meia), (a, meia) })
            {
                var (x, y) = NaParede(w, t, d);
                var p = P(x, y);
                ret.AddVertexAt(i++, new Point2d(p.X, p.Y), 0, 0, 0);
            }
            Adicionar(ret);

            double l = Math.Round(b - a);
            string medida = porta ? $"{Num(l)}x{Num(cfg.AlturaPorta)}" : MedidaJanela(v, cfg, l);
            int fora = Anotacoes.Exterior(planta, w, 1, (a + b) / 2) ? 1 : Anotacoes.Exterior(planta, w, -1, (a + b) / 2) ? -1 : 1;
            double rot = w.Horizontal ? 0 : Math.PI / 2;
            var (xa, ya) = NaParede(w, (a + b) / 2, fora * (meia + 12));
            Texto("ABERTURA", xa, ya, 3, rot, layer);
            var (xm, ym) = NaParede(w, (a + b) / 2, -fora * (meia + 12));
            Texto(medida, xm, ym, 3, rot, layer);
        }

        /// <summary>"151 x 100 / 120" a partir do quadro de janelas da obra (largura desenhada → resposta do projetista).</summary>
        public static string MedidaJanela(Vao v, ConfigObra cfg, double abertura)
        {
            string chave = Num(Math.Round(v.Largura / 5) * 5);
            if (cfg.Janelas.TryGetValue(chave, out var resp))
            {
                var nums = Regex.Matches(resp ?? "", @"\d+([.,]\d+)?").Cast<Match>()
                    .Select(m => double.Parse(m.Value.Replace(',', '.'), CultureInfo.InvariantCulture)).ToList();
                if (nums.Count >= 3) return $"{Num(abertura)} x {Num(nums[1])} / {Num(nums[2])}";
            }
            return Num(abertura);
        }

        /// <summary>"PAREDE EM DRY WALL" no meio do trecho retirado.</summary>
        public void Drywall((Orientacao orientacao, double coord, double inicio, double fim) trecho)
        {
            string layer = Camadas.Garantir(_db, _tr, LayerAberturas, 7);
            double meio = (trecho.inicio + trecho.fim) / 2;
            bool horizontal = trecho.orientacao == Orientacao.Horizontal;
            var (x, y) = horizontal ? (meio, trecho.coord - 20) : (trecho.coord + 20, meio);
            Texto("PAREDE EM DRY WALL", x, y, 3, horizontal ? 0 : Math.PI / 2, layer);
        }

        // ------------------------------------------------------------------ cotas e título

        /// <summary>Cotas internas: em cadeia na face de cada parede, dentro do cômodo, medidas em cm.</summary>
        public void CotasInternas(IEnumerable<LinhaCota> linhas, double larguraCm)
        {
            string layer = Camadas.Garantir(_db, _tr, LayerCotas, 4);
            var estilo = Camadas.Cota(_db, _tr, _fator, _escala);
            foreach (var c in linhas)
            {
                var w = c.Parede;
                double face = c.Lado * larguraCm / 2;
                double linha = c.Lado * (larguraCm / 2 + c.Afastamento);
                for (int i = 0; i + 1 < c.Pontos.Count; i++)
                {
                    var (x1, y1) = NaParede(w, c.Pontos[i], face);
                    var (x2, y2) = NaParede(w, c.Pontos[i + 1], face);
                    var (xl, yl) = NaParede(w, c.Pontos[i], linha);
                    var cota = new AlignedDimension(P(x1, y1), P(x2, y2), P(xl, yl), "", estilo) { Layer = layer };
                    Adicionar(cota);
                    Cotas++;
                }
            }
        }

        /// <summary>
        /// Cota de uma parede avulsa (ALV1FIADA/ALV2FIADA): em cadeia entre pontos do eixo (unidades do desenho),
        /// medida na face de baixo/da direita, e o total por fora quando há aberturas no meio.
        /// </summary>
        public void CotarTrecho(Trecho eixo, IEnumerable<Point3d> pontos, double larguraCm)
        {
            var ordenados = pontos.OrderBy(p => (p - eixo.Inicio).DotProduct(eixo.Direcao)).ToList();
            if (ordenados.Count < 2) return;
            string layer = Camadas.Garantir(_db, _tr, LayerCotas, 4);
            var estilo = Camadas.Cota(_db, _tr, _fator, _escala);
            var face = -eixo.Perpendicular * (larguraCm / 2 / _fator);

            void Cadeia(IList<Point3d> pts, double afastamentoMm)
            {
                var lado = -eixo.Perpendicular * Mm(afastamentoMm);
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    if (pts[i].DistanceTo(pts[i + 1]) < 1e-6) continue;
                    var p1 = pts[i] + face + _deslocamento;
                    var p2 = pts[i + 1] + face + _deslocamento;
                    Adicionar(new AlignedDimension(p1, p2, p1 + lado, "", estilo) { Layer = layer });
                    Cotas++;
                }
            }
            Cadeia(ordenados, 8);
            if (ordenados.Count > 2) Cadeia(new[] { ordenados[0], ordenados[ordenados.Count - 1] }, 16);
        }

        /// <summary>Título embaixo da planta, alinhado à direita: "Planta de 1ª fiada - Modulação" e a escala.</summary>
        public void Titulo(string titulo, double xMaxCm, double yMinCm)
        {
            string layer = Camadas.Garantir(_db, _tr, LayerTitulos, 6);
            double y1 = yMinCm - Mm(14) * _fator;
            Texto(titulo, xMaxCm, y1, 7, 0, layer, TextHorizontalMode.TextRight, TextVerticalMode.TextBottom);
            Texto($"escala 1:{Num(_escala)}", xMaxCm, y1 - Mm(10) * _fator, 5, 0, layer, TextHorizontalMode.TextRight, TextVerticalMode.TextBottom);
        }

        private static string Num(double v) => v.ToString("0.##", CultureInfo.InvariantCulture);
    }
}
