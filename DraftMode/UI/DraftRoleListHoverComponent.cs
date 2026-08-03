
using Reactor.Utilities.Attributes;
using TMPro;
using TownOfUs.Modules.Components;
using TownOfUs.Options;
using TownOfUs.Patches;
using UnityEngine;

namespace DraftMode
{
    [RegisterInIl2Cpp]
    public sealed class DraftRoleListHoverComponent(nint cppPtr) : MonoBehaviour(cppPtr)
    {
        public TextMeshPro TextTarget;

        private int _lastLine = -1;
        private string _originalText = string.Empty;

        private GameObject _tooltipGo;
        private TextMeshPro _tooltipTmp;
        private AspectPosition _tooltipAp;
        private string _tooltipBaseText = string.Empty;

        private const float BaseX = 0.43f;
        private float _hideDelay;
        private const float HideDelayDuration = 0.3f;
        private const float BaseY = 0.1f;

        public void Update()
        {
            if (!DraftLobbyRoleListPatch.IsShowingRoleListPool() || TextTarget == null || !TextTarget.gameObject.activeSelf)
            {
                _lastLine = -1;
                RestoreRoleListText();
                HideTooltip();
                return;
            }

            EnsureTooltip();

            if (_tooltipGo != null && _tooltipGo.activeSelf)
            {
                UpdateTooltipLinks();
            }

            var line = GetLineUnderMouse();

            if (line == _lastLine)
            {
                _hideDelay = 0f;
                return;
            }

            if (line < 0)
            {
                _hideDelay += Time.deltaTime;
                if (_hideDelay < HideDelayDuration) return;
                _hideDelay = 0f;
            }
            else
            {
                _hideDelay = 0f;
            }

            _lastLine = line;
            RestoreRoleListText();

            if (line < 0)
            {
                HideTooltip();
                return;
            }

            var bucket = DraftLobbyRoleListPatch.BucketForLine(line);
            if (bucket == null)
            {
                HideTooltip();
                return;
            }

            ShowTooltipForBucket(bucket.Value, line);
        }

        public void OnDisable()
        {
            _lastLine = -1;
            RestoreRoleListText();
            HideTooltip();
        }

        private void UpdateTooltipLinks()
        {
            if (_tooltipTmp == null || _tooltipBaseText == string.Empty) return;

            var cam = Camera.main;
            if (cam == null) return;

            _tooltipTmp.text = _tooltipBaseText;

            var linkIndex = TMP_TextUtilities.FindIntersectingLink(
                _tooltipTmp, Input.mousePosition, cam);

            if (linkIndex >= 0 && linkIndex < _tooltipTmp.textInfo.linkCount)
            {
                var linkInfo = _tooltipTmp.textInfo.linkInfo[linkIndex];
                var linkId = linkInfo.GetLinkID();

                _tooltipTmp.text = _tooltipBaseText.Replace(
                    $"<link=\"{linkId}\">",
                    $"<link=\"{linkId}\"><i>").Replace(
                    $"</link>",
                    $"</i></link>", System.StringComparison.Ordinal);

                if (Input.GetMouseButtonDown(0))
                {
                    if (Minigame.Instance)
                        return;
                    WikiHyperlink.OpenHyperlink(linkInfo);
                }
            }
        }

        private void EnsureTooltip()
        {
            if (_tooltipGo != null) return;

            var pingTracker = UnityEngine.Object.FindObjectOfType<PingTracker>(true);
            if (pingTracker == null) return;

            _tooltipGo = UnityEngine.Object.Instantiate(pingTracker.gameObject, HudManager.Instance.transform);
            _tooltipGo.name = "DraftBucketTooltipText";

            var pt = _tooltipGo.GetComponent<PingTracker>();
            if (pt != null) UnityEngine.Object.Destroy(pt);

            _tooltipAp = _tooltipGo.GetComponent<AspectPosition>();
            _tooltipAp.Alignment = AspectPosition.EdgeAlignments.LeftTop;
            _tooltipAp.DistanceFromEdge = new Vector3(BaseX, BaseY, 1f);
            _tooltipAp.AdjustPosition();

            _tooltipTmp = _tooltipGo.GetComponent<TextMeshPro>();
            _tooltipTmp.alignment = TextAlignmentOptions.TopLeft;
            _tooltipTmp.verticalAlignment = VerticalAlignmentOptions.Top;
            _tooltipTmp.fontSize = _tooltipTmp.fontSizeMin = _tooltipTmp.fontSizeMax = TextTarget.fontSize;
            _tooltipTmp.enableWordWrapping = false;
            _tooltipTmp.text = "";

            _tooltipGo.SetActive(false);
        }

        private void HideTooltip()
        {
            if (_tooltipGo != null)
                _tooltipGo.SetActive(false);
            _tooltipBaseText = string.Empty;
        }

        private int GetLineUnderMouse()
        {
            if (TextTarget.textInfo == null || TextTarget.textInfo.lineCount == 0) return -1;

            var cam = Camera.main;
            if (cam == null) return -1;

            if (_tooltipGo != null && _tooltipGo.activeSelf && IsMouseOverTooltip())
                return _lastLine;

            var worldPos = cam.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x,
                Input.mousePosition.y,
                -cam.transform.position.z));
            var localPos = TextTarget.transform.InverseTransformPoint(worldPos);

            var bounds = TextTarget.bounds;
            if (localPos.x < bounds.min.x || localPos.x > bounds.max.x)
                return -1;

            for (var i = 0; i < TextTarget.textInfo.lineCount; i++)
            {
                var li = TextTarget.textInfo.lineInfo[i];
                if (li.characterCount == 0) continue;
                if (localPos.y <= li.ascender && localPos.y >= li.descender)
                    return i;
            }

            return -1;
        }

        private bool IsMouseOverTooltip()
        {
            if (_tooltipTmp == null) return false;
            var cam = Camera.main;
            if (cam == null) return false;

            var worldPos = cam.ScreenToWorldPoint(new Vector3(
                Input.mousePosition.x,
                Input.mousePosition.y,
                -cam.transform.position.z));
            var localPos = _tooltipTmp.transform.InverseTransformPoint(worldPos);
            var bounds = _tooltipTmp.bounds;

            return localPos.x >= bounds.min.x && localPos.x <= bounds.max.x &&
                   localPos.y >= bounds.min.y && localPos.y <= bounds.max.y;
        }

        private void ShowTooltipForBucket(RoleListOption bucket, int hoveredLine)
        {
            if (!BucketTooltipData.TryGet(bucket, out var info)) return;

            if (_originalText == string.Empty)
                _originalText = TextTarget.text;

            var lines = _originalText.Split('\n');
            if (hoveredLine < lines.Length)
            {
                var copy = (string[])lines.Clone();
                copy[hoveredLine] = $"<i>{copy[hoveredLine]}</i>";
                TextTarget.text = string.Join("\n", copy);
            }

            var lineInfo = TextTarget.textInfo.lineInfo[hoveredLine];
            var lineWorldY = TextTarget.transform.TransformPoint(new Vector3(0, lineInfo.ascender, 0)).y;
            var lineWorldX = TextTarget.transform.TransformPoint(new Vector3(TextTarget.bounds.max.x, 0, 0)).x;

            var cam = Camera.main;
            var camTop = cam.transform.position.y + cam.orthographicSize;
            var camLeft = cam.transform.position.x - cam.orthographicSize * cam.aspect;

            var yOffset = camTop - lineWorldY;
            var xOffset = lineWorldX - camLeft + 0.4f;

            _tooltipAp.DistanceFromEdge = new Vector3(xOffset, yOffset, 1f);
            _tooltipAp.AdjustPosition();

            _tooltipBaseText = BucketTooltipData.BuildTooltipText(in info);
            _tooltipTmp.text = _tooltipBaseText;
            _tooltipTmp.ForceMeshUpdate();
            _tooltipGo.SetActive(true);

            HudManagerPatches.IsHoveringRoleList = true;
        }

        private void RestoreRoleListText()
        {
            HudManagerPatches.IsHoveringRoleList = false;

            if (_originalText != string.Empty && TextTarget != null)
            {
                TextTarget.text = _originalText;
                _originalText = string.Empty;
            }
        }
    }
}
