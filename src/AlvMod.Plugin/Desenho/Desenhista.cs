using System;
using System.Collections.Generic;
using System.Linq;
using AlvMod.Core.Catalogo;
using AlvMod.Core.Modulacao;
using Autodesk.AutoCAD.Colors;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Desenha peças e cotas de uma fiada.</summary>
    internal sealed class Desenhista
    {
        public const string LayerCotas = "COTAS ALVENARIA";

        private readonly Database _db;
        private readonly Transaction _tr;
        private readonly BlockTableRecord _espaco;
        private readonly double _fator;
        private readonly Vector3d _deslocamento;

        /// <summary>Blocos que não existiam no desenho e foram criados como retângulo provisório.</summary>
        public HashSet<string> BlocosProvisorios { get; } = new HashSet<string>();

        /// <summary>
        /// Blocos do desenho cujo tamanho real difere do catálogo: foram ajustados na inserção para as peças
        /// ficarem a 1 cm uma da outra. Nome → descrição da diferença.
        /// </summary>
        public Dictionary<string, string> BlocosAjustados { get; } = new Dictionary<string, string>();

        public int PecasInseridas { get; private set; }

        private readonly Dictionary<string, InfoBloco> _info = new Dictionary<string, InfoBloco>();

        private sealed class InfoBloco
        {
            public ObjectId Id;
            public double MinX, MinY, Comprimento, Largura;
        }

        /// <param name="fator">Quantos cm vale 1 unidade do desenho (1 = cm, 100 = m, 0.1 = mm).</param>
        /// <param name="deslocamento">Translação aplicada a tudo que for desenhado (planta da 2ª fiada).</param>
        public Desenhista(Database db, Transaction tr, double fator, Vector3d deslocamento = default)
        {
            _db = db;
            _tr = tr;
            _fator = fator;
            _deslocamento = deslocamento;
            _espaco = (BlockTableRecord)tr.GetObject(db.CurrentSpaceId, OpenMode.ForWrite);
        }

        /// <summary>
        /// Insere o bloco DWG da peça. O canto inferior esquerdo do bloco (pelas medidas reais dele) vai
        /// para o canto da peça na parede, e a escala é ajustada se o bloco do desenho não tiver o tamanho
        /// do catálogo (ex.: 40 em vez de 39, ou desenhado em metros): assim as peças ficam sempre a 1 cm.
        /// Se o bloco não existir, é criado um provisório (retângulo) com o mesmo nome.
        /// </summary>
        public void Desenhar(Trecho trecho, PecaPosicionada item)
        {
            var peca = item.Peca;
            string layer = GarantirLayer(peca.Layer, peca.Cor);
            var info = GarantirBloco(peca);

            double sx = 1.0 / _fator, sy = 1.0 / _fator;
            if (info.Comprimento > 0 && Math.Abs(info.Comprimento - peca.Comprimento) > 0.2)
                sx *= peca.Comprimento / info.Comprimento;
            if (info.Largura > 0 && Math.Abs(info.Largura - peca.Largura) > 0.2)
                sy *= peca.Largura / info.Largura;

            double meia = peca.Largura / 2.0 / _fator;
            var canto = trecho.PontoEm(item.Inicio / _fator) - trecho.Perpendicular * meia + _deslocamento;

            // O ponto de inserção do bloco pode não ser o canto: desloca pela posição real do canto dele.
            double rot = Math.Atan2(trecho.Direcao.Y, trecho.Direcao.X);
            var u = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);
            var v = new Vector3d(-Math.Sin(rot), Math.Cos(rot), 0);
            var posicao = canto - u * (info.MinX * sx) - v * (info.MinY * sy);

            var br = new BlockReference(posicao, info.Id)
            {
                Rotation = rot,
                ScaleFactors = new Scale3d(sx, sy, sx),
                Layer = layer
            };
            Adicionar(br);
            PecasInseridas++;
        }

        /// <summary>
        /// Cotas alinhadas em cadeia ao longo da face da parede: cada trecho de alvenaria e cada vão entre eles
        /// (parede até o vão, o vão, do vão até o fim da parede — questionário, item 47).
        /// </summary>
        /// <param name="escala">Denominador da escala de impressão (50 para 1:50).</param>
        /// <param name="larguraCm">Largura da parede: as cotas medem pela face, não pelo eixo.</param>
        public int Cotar(IList<Trecho> trechos, double escala, double larguraCm)
        {
            if (trechos.Count == 0) return 0;
            var refT = trechos[0];
            var pontos = trechos.SelectMany(t => new[] { t.Inicio, t.Fim }).ToList();
            return CotarPontos(refT, pontos, escala, larguraCm);
        }

        /// <summary>Cota em cadeia entre pontos consecutivos (já sobre o eixo da parede de referência).</summary>
        public int CotarPontos(Trecho referencia, IEnumerable<Point3d> pontos, double escala, double larguraCm)
        {
            var refT = referencia;
            var ordenados = pontos
                .OrderBy(p => (p - refT.Inicio).DotProduct(refT.Direcao))
                .ToList();
            if (ordenados.Count < 2) return 0;

            // Tamanhos pensados em papel (mm) e convertidos para o desenho: dimscale = escala / (10 × fator).
            double dimscale = escala / (10.0 * _fator);
            double afastamento = 8.0 * dimscale; // 8 mm no papel, abaixo/à direita da parede
            var face = -refT.Perpendicular * (larguraCm / 2.0 / _fator); // face do lado da cota
            var lado = -refT.Perpendicular * afastamento;
            string layer = GarantirLayer(LayerCotas, 2);

            int n = 0;
            for (int i = 0; i + 1 < ordenados.Count; i++)
            {
                if (ordenados[i].DistanceTo(ordenados[i + 1]) < 1e-6) continue;
                var p1 = ordenados[i] + face + _deslocamento;
                var p2 = ordenados[i + 1] + face + _deslocamento;
                var cota = new AlignedDimension(p1, p2, p1 + lado, "", _db.Dimstyle)
                {
                    Layer = layer,
                    Dimscale = dimscale
                };
                Adicionar(cota);
                n++;
            }
            return n;
        }

        private void Adicionar(Entity ent)
        {
            _espaco.AppendEntity(ent);
            _tr.AddNewlyCreatedDBObject(ent, true);
        }

        private InfoBloco GarantirBloco(Peca peca)
        {
            if (_info.TryGetValue(peca.NomeBlocoDwg, out var feito)) return feito;

            var bt = (BlockTable)_tr.GetObject(_db.BlockTableId, OpenMode.ForRead);
            InfoBloco info;
            if (bt.Has(peca.NomeBlocoDwg))
            {
                info = Medir(bt[peca.NomeBlocoDwg], peca);
                bool difC = Math.Abs(info.Comprimento - peca.Comprimento) > 0.2;
                bool difL = Math.Abs(info.Largura - peca.Largura) > 0.2;
                if (difC || difL)
                    BlocosAjustados[peca.NomeBlocoDwg] =
                        $"mede {info.Comprimento:0.##} x {info.Largura:0.##} no desenho e {peca.Comprimento} x {peca.Largura} no catálogo";
            }
            else
            {
                bt.UpgradeOpen();
                var btr = new BlockTableRecord { Name = peca.NomeBlocoDwg, Origin = Point3d.Origin };
                var id = bt.Add(btr);
                _tr.AddNewlyCreatedDBObject(btr, true);

                var ret = new Polyline(4) { Closed = true, Layer = "0" }; // layer 0: herda o layer do INSERT
                ret.AddVertexAt(0, new Point2d(0, 0), 0, 0, 0);
                ret.AddVertexAt(1, new Point2d(peca.Comprimento, 0), 0, 0, 0);
                ret.AddVertexAt(2, new Point2d(peca.Comprimento, peca.Largura), 0, 0, 0);
                ret.AddVertexAt(3, new Point2d(0, peca.Largura), 0, 0, 0);
                btr.AppendEntity(ret);
                _tr.AddNewlyCreatedDBObject(ret, true);

                BlocosProvisorios.Add(peca.NomeBlocoDwg);
                info = new InfoBloco { Id = id, MinX = 0, MinY = 0, Comprimento = peca.Comprimento, Largura = peca.Largura };
            }
            _info[peca.NomeBlocoDwg] = info;
            return info;
        }

        /// <summary>Extensão real do desenho do bloco (canto inferior esquerdo, comprimento e largura).</summary>
        private InfoBloco Medir(ObjectId id, Peca peca)
        {
            var info = new InfoBloco { Id = id, MinX = 0, MinY = 0, Comprimento = peca.Comprimento, Largura = peca.Largura };
            try
            {
                var btr = (BlockTableRecord)_tr.GetObject(id, OpenMode.ForRead);
                Extents3d? ext = null;
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
                if (ext != null)
                {
                    var e = ext.Value;
                    info.MinX = e.MinPoint.X;
                    info.MinY = e.MinPoint.Y;
                    info.Comprimento = e.MaxPoint.X - e.MinPoint.X;
                    info.Largura = e.MaxPoint.Y - e.MinPoint.Y;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Mantém as medidas do catálogo.
            }
            return info;
        }

        private string GarantirLayer(string nome, short cor)
        {
            var lt = (LayerTable)_tr.GetObject(_db.LayerTableId, OpenMode.ForRead);
            if (lt.Has(nome)) return nome; // layer existente: mantém a cor do escritório

            lt.UpgradeOpen();
            var novo = new LayerTableRecord
            {
                Name = nome,
                Color = Color.FromColorIndex(ColorMethod.ByAci, cor is >= 1 and <= 255 ? cor : (short)7)
            };
            lt.Add(novo);
            _tr.AddNewlyCreatedDBObject(novo, true);
            return nome;
        }
    }
}
