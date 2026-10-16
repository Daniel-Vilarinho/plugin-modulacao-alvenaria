using System;
using System.Collections.Generic;
using System.Globalization;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;

namespace AlvMod.Plugin.Desenho
{
    /// <summary>Trecho reto do eixo de uma parede, em unidades do desenho.</summary>
    internal sealed class Trecho
    {
        public Point3d Inicio { get; }
        public Point3d Fim { get; }
        public Vector3d Direcao { get; }
        public Vector3d Perpendicular { get; }
        public double Comprimento { get; }

        public Trecho(Point3d inicio, Point3d fim)
        {
            Inicio = inicio;
            Fim = fim;
            Comprimento = inicio.DistanceTo(fim);
            Direcao = (fim - inicio).GetNormal();
            Perpendicular = new Vector3d(-Direcao.Y, Direcao.X, 0);
        }

        /// <summary>
        /// Mesmo trecho com início na ponta esquerda (ou inferior, se vertical), seguindo a origem
        /// "canto inferior esquerdo" do questionário (item 23).
        /// </summary>
        public Trecho Normalizado()
        {
            const double eps = 1e-9;
            bool inverter = Direcao.X < -eps || (Math.Abs(Direcao.X) <= eps && Direcao.Y < 0);
            return inverter ? new Trecho(Fim, Inicio) : this;
        }

        /// <summary>Posição ao longo do eixo (t) e afastamento lateral (d) de um ponto.</summary>
        public void Projetar(Point3d p, out double t, out double d)
        {
            var v = p - Inicio;
            t = v.DotProduct(Direcao);
            d = v.DotProduct(Perpendicular);
        }

        public Point3d PontoEm(double distancia) => Inicio + Direcao * distancia;

        public string Descrever() => string.Format(CultureInfo.InvariantCulture,
            "de ({0:0.##}, {1:0.##}) a ({2:0.##}, {3:0.##})", Inicio.X, Inicio.Y, Fim.X, Fim.Y);
    }

    internal static class Geometria
    {
        /// <summary>
        /// Pede o eixo da parede (LINE ou segmento reto de POLYLINE) ou, com a opção Faces, as duas faces
        /// paralelas — nesse caso o eixo é a linha média entre elas.
        /// </summary>
        public static Trecho SelecionarTrecho(Editor ed, Transaction tr, string mensagem)
        {
            var opcoes = new PromptEntityOptions(mensagem + " [Faces]: ");
            opcoes.Keywords.Add("Faces");
            opcoes.AppendKeywordsToMessage = false;
            opcoes.AllowNone = true;
            opcoes.SetRejectMessage("\nSelecione uma LINE ou POLYLINE.");
            opcoes.AddAllowedClass(typeof(Line), true);
            opcoes.AddAllowedClass(typeof(Polyline), true);

            var res = ed.GetEntity(opcoes);
            if (res.Status == PromptStatus.Keyword) return SelecionarPorFaces(ed, tr);
            if (res.Status != PromptStatus.OK) return null;
            return SegmentoDe(ed, tr, res);
        }

        private static Trecho SelecionarPorFaces(Editor ed, Transaction tr)
        {
            var face1 = SelecionarSegmento(ed, tr, "\nSelecione a 1ª face da parede: ");
            if (face1 == null) return null;
            var face2 = SelecionarSegmento(ed, tr, "\nSelecione a 2ª face (paralela): ");
            if (face2 == null) return null;

            if (Math.Abs(face1.Direcao.DotProduct(face2.Direcao)) < Math.Cos(2 * Math.PI / 180))
            {
                ed.WriteMessage("\nAs faces não são paralelas.");
                return null;
            }

            // Eixo = média entre cada ponta da face 1 e a projeção dela sobre a reta da face 2.
            var reta2 = new Line3d(face2.Inicio, face2.Fim);
            var a = face1.Inicio;
            var b = face1.Fim;
            var meioA = a + (reta2.GetClosestPointTo(a).Point - a) / 2;
            var meioB = b + (reta2.GetClosestPointTo(b).Point - b) / 2;
            double espessura = a.DistanceTo(reta2.GetClosestPointTo(a).Point);
            ed.WriteMessage(string.Format(CultureInfo.InvariantCulture,
                "\nEixo calculado entre as faces (espessura {0:0.##} unidades).", espessura));
            return new Trecho(meioA, meioB);
        }

        private static Trecho SelecionarSegmento(Editor ed, Transaction tr, string mensagem)
        {
            var opcoes = new PromptEntityOptions(mensagem);
            opcoes.SetRejectMessage("\nSelecione uma LINE ou POLYLINE.");
            opcoes.AddAllowedClass(typeof(Line), true);
            opcoes.AddAllowedClass(typeof(Polyline), true);
            var res = ed.GetEntity(opcoes);
            return res.Status == PromptStatus.OK ? SegmentoDe(ed, tr, res) : null;
        }

        private static Trecho SegmentoDe(Editor ed, Transaction tr, PromptEntityResult res)
        {
            var ent = (Entity)tr.GetObject(res.ObjectId, OpenMode.ForRead);
            if (ent is Line linha)
                return new Trecho(linha.StartPoint, linha.EndPoint);

            var pl = (Polyline)ent;
            var clique = res.PickedPoint.TransformBy(ed.CurrentUserCoordinateSystem);
            var maisProximo = pl.GetClosestPointTo(clique, Vector3d.ZAxis, false);
            int segmentos = pl.Closed ? pl.NumberOfVertices : pl.NumberOfVertices - 1;
            int i = Math.Min(Math.Max((int)Math.Floor(pl.GetParameterAtPoint(maisProximo)), 0), segmentos - 1);

            if (pl.GetSegmentType(i) != SegmentType.Line)
            {
                ed.WriteMessage("\nO segmento selecionado é um arco. Só paredes retas são suportadas por enquanto.");
                return null;
            }
            var seg = pl.GetLineSegmentAt(i);
            if (segmentos > 1)
                ed.WriteMessage($"\nPolilinha com {segmentos} segmentos: usando o segmento {i + 1}, o mais próximo do clique.");
            return new Trecho(seg.StartPoint, seg.EndPoint);
        }

        /// <summary>Cantos de uma peça desenhada (bloco DWG ou retângulo POLYLINE), em coordenadas do desenho.</summary>
        public static List<Point3d> Cantos(Entity ent, Transaction tr)
        {
            var pontos = new List<Point3d>();
            if (ent is Polyline pl)
            {
                // Só retângulos fechados: ignora o próprio eixo e outras polilinhas que vierem na seleção.
                if (!pl.Closed || pl.NumberOfVertices != 4) return pontos;
                for (int i = 0; i < pl.NumberOfVertices; i++) pontos.Add(pl.GetPoint3dAt(i));
            }
            else if (ent is BlockReference br)
            {
                // Extensão da definição do bloco, transformada pela inserção (funciona em parede inclinada).
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                var ext = new Extents3d();
                bool vazio = true;
                foreach (ObjectId id in btr)
                {
                    if (!(tr.GetObject(id, OpenMode.ForRead) is Entity filho) || filho is AttributeDefinition) continue;
                    try
                    {
                        if (vazio) { ext = filho.GeometricExtents; vazio = false; }
                        else ext.AddExtents(filho.GeometricExtents);
                    }
                    catch (Autodesk.AutoCAD.Runtime.Exception) { /* entidade sem extensão */ }
                }
                if (vazio) return pontos;

                var min = ext.MinPoint;
                var max = ext.MaxPoint;
                foreach (var p in new[]
                         {
                             new Point3d(min.X, min.Y, 0), new Point3d(max.X, min.Y, 0),
                             new Point3d(max.X, max.Y, 0), new Point3d(min.X, max.Y, 0)
                         })
                    pontos.Add(p.TransformBy(br.BlockTransform));
            }
            return pontos;
        }
    }
}
