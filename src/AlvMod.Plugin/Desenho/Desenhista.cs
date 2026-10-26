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
    /// <summary>Desenha as peças de uma fiada (blocos do catálogo).</summary>
    internal sealed class Desenhista
    {

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

            /// <summary>
            /// Bloco desenhado "em pé": o comprimento da peça no eixo Y do bloco e a largura da parede no X
            /// (é como a bolacha e o Bloco 9x14 do engenheiro estão desenhados: 14 × 4 e 14 × 9).
            /// </summary>
            public bool Girado;
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

            // Escala de cada eixo do bloco: medida da peça no catálogo ÷ medida do desenho do bloco.
            double escComp = info.Comprimento > 0 ? peca.Comprimento / _fator / info.Comprimento : 1.0 / _fator;
            double escLarg = info.Largura > 0 ? peca.Largura / _fator / info.Largura : 1.0 / _fator;

            double meia = peca.Largura / 2.0 / _fator;
            var noEixo = trecho.PontoEm(item.Inicio / _fator) + _deslocamento;
            double rot = Math.Atan2(trecho.Direcao.Y, trecho.Direcao.X);
            var u = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0);   // ao longo da parede
            var v = new Vector3d(-Math.Sin(rot), Math.Cos(rot), 0);  // atravessando a parede

            BlockReference br;
            if (!info.Girado)
            {
                // X do bloco ao longo da parede: o canto inferior esquerdo do desenho do bloco vai no canto da peça.
                var posicao = noEixo - v * meia - u * (info.MinX * escComp) - v * (info.MinY * escLarg);
                br = new BlockReference(posicao, info.Id) { Rotation = rot, ScaleFactors = new Scale3d(escComp, escLarg, escComp) };
            }
            else
            {
                // Bloco "em pé" girado de −90°: o Y dele vai ao longo da parede e o X atravessa.
                var posicao = noEixo - u * (info.MinY * escComp) + v * (meia + info.MinX * escLarg);
                br = new BlockReference(posicao, info.Id)
                {
                    Rotation = rot - Math.PI / 2,
                    ScaleFactors = new Scale3d(escLarg, escComp, escLarg)
                };
            }
            br.Layer = layer;
            Adicionar(br);
            PecasInseridas++;
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
            var existente = BlocoDoDesenho(bt, peca.NomeBlocoDwg);
            if (!existente.IsNull)
            {
                info = Medir(existente, peca);
                // Blocos desenhados em metros ou numa escala qualquer são normais (o engenheiro usa blocos em
                // metro numa planta em que 1 unidade = 25 cm): só avisa quando a PROPORÇÃO não bate.
                double proporcaoBloco = info.Largura > 0 ? info.Comprimento / info.Largura : 0;
                double proporcaoPeca = (double)peca.Comprimento / peca.Largura;
                if (proporcaoBloco > 0 && Math.Abs(proporcaoBloco / proporcaoPeca - 1) > 0.05)
                    BlocosAjustados[peca.NomeBlocoDwg] =
                        $"tem proporção {info.Comprimento:0.###} x {info.Largura:0.###} e a peça {peca.Comprimento} x {peca.Largura}";
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

        /// <summary>
        /// Bloco do desenho com esse nome, sem diferenciar maiúsculas, espaços e hífens ("P-Bloco 34" = "P-Bloco34"),
        /// porque cada biblioteca grava de um jeito. Nulo se não existir.
        /// </summary>
        private ObjectId BlocoDoDesenho(BlockTable bt, string nome)
        {
            if (bt.Has(nome)) return bt[nome];
            string chave = Familia.NormalizarNome(nome);
            foreach (ObjectId id in bt)
            {
                var btr = (BlockTableRecord)_tr.GetObject(id, OpenMode.ForRead);
                if (btr.IsLayout || btr.IsAnonymous || btr.IsFromExternalReference) continue;
                if (Familia.NormalizarNome(btr.Name) == chave) return id;
            }
            return ObjectId.Null;
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
                    double dx = e.MaxPoint.X - e.MinPoint.X, dy = e.MaxPoint.Y - e.MinPoint.Y;
                    info.MinX = e.MinPoint.X;
                    info.MinY = e.MinPoint.Y;
                    // Peça mais curta que a largura da parede (bolacha) desenhada "em pé": X = largura, Y = comprimento.
                    double proporcao = (double)peca.Comprimento / peca.Largura;
                    info.Girado = peca.Comprimento < peca.Largura && dx > dy &&
                                  Math.Abs(dy / dx - proporcao) < Math.Abs(dx / dy - proporcao);
                    info.Comprimento = info.Girado ? dy : dx;
                    info.Largura = info.Girado ? dx : dy;
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception)
            {
                // Mantém as medidas do catálogo.
            }
            return info;
        }

        private string GarantirLayer(string nome, short cor) => Camadas.Garantir(_db, _tr, nome, cor);
    }
}
