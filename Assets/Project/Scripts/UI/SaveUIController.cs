using UnityEngine;
using UnityEngine.UI;
using TMPro;
using MinecraftClone.Management;

namespace MinecraftClone.UI
{
    public class SaveUIController : MonoBehaviour
    {
        [Header("UI Слотов (5 штук)")]
        [SerializeField] private Button[] slotSaveButtons = new Button[5];
        [SerializeField] private TextMeshProUGUI[] slotLabels = new TextMeshProUGUI[5];
        [SerializeField] private Button[] slotLoadButtons = new Button[5];

        [Header("Модальное окно предупреждения")]
        [SerializeField] private GameObject warningModal;
        [SerializeField] private TextMeshProUGUI warningText;
        [SerializeField] private Button confirmOverwriteButton;
        [SerializeField] private Button cancelOverwriteButton;

        private int pendingSlotIndex = -1;

        private void Start()
        {
            InitUI();
        }

        private void InitUI()
        {
            if (warningModal != null) warningModal.SetActive(false);

            for (int i = 0; i < 5; i++)
            {
                int slotIndex = i + 1;
                int arrayIndex = i;

                if (arrayIndex < slotSaveButtons.Length && slotSaveButtons[arrayIndex] != null)
                {
                    slotSaveButtons[arrayIndex].onClick.RemoveAllListeners();
                    slotSaveButtons[arrayIndex].onClick.AddListener(() => OnSaveSlotClicked(slotIndex));
                }

                if (arrayIndex < slotLoadButtons.Length && slotLoadButtons[arrayIndex] != null)
                {
                    slotLoadButtons[arrayIndex].onClick.RemoveAllListeners();
                    slotLoadButtons[arrayIndex].onClick.AddListener(() => OnLoadSlotClicked(slotIndex));
                }

                RefreshSlotUI(slotIndex);
            }

            if (confirmOverwriteButton != null)
            {
                confirmOverwriteButton.onClick.RemoveAllListeners();
                confirmOverwriteButton.onClick.AddListener(OnConfirmOverwrite);
            }

            if (cancelOverwriteButton != null)
            {
                cancelOverwriteButton.onClick.RemoveAllListeners();
                cancelOverwriteButton.onClick.AddListener(OnCancelOverwrite);
            }
        }

        private void RefreshSlotUI(int slotIndex)
        {
            int arrayIndex = slotIndex - 1;

            if (arrayIndex < 0 || arrayIndex >= slotLabels.Length) return;

            bool exists = SaveSystem.DoesSlotExist(slotIndex);

            if (slotLabels[arrayIndex] != null)
            {
                slotLabels[arrayIndex].text = exists ? $"Мир {slotIndex}" : "- пусто -";
            }
            else
            {
                Debug.LogWarning($"[SaveUIController] Element {arrayIndex} в 'Slot Labels' НЕ привязан в Инспекторе Unity!");
            }

            if (arrayIndex < slotLoadButtons.Length && slotLoadButtons[arrayIndex] != null)
            {
                slotLoadButtons[arrayIndex].interactable = exists;
            }
        }

        private void OnSaveSlotClicked(int slotIndex)
        {
            if (SaveSystem.DoesSlotExist(slotIndex))
            {
                pendingSlotIndex = slotIndex;
                if (warningText != null)
                {
                    warningText.text = $"Перезаписать Мир {slotIndex}?\nТекущий сохраненный мир будет полностью уничтожен!";
                }
                if (warningModal != null) warningModal.SetActive(true);
            }
            else
            {
                ExecuteSave(slotIndex);
            }
        }

        private void OnConfirmOverwrite()
        {
            if (warningModal != null) warningModal.SetActive(false);
            if (pendingSlotIndex > 0)
            {
                ExecuteSave(pendingSlotIndex);
                pendingSlotIndex = -1;
            }
        }

        private void OnCancelOverwrite()
        {
            if (warningModal != null) warningModal.SetActive(false);
            pendingSlotIndex = -1;
        }

        private void ExecuteSave(int slotIndex)
        {
            if (WorldManager.Instance == null || SaveSystem.Instance == null) return;

            Vector3 playerPos = Vector3.zero;
            Vector2 playerRot = Vector2.zero;

            GameObject playerObj = GameObject.FindGameObjectWithTag("Player");
            if (playerObj != null)
            {
                playerPos = playerObj.transform.position;
                playerRot = new Vector2(playerObj.transform.eulerAngles.x, playerObj.transform.eulerAngles.y);
            }
            else if (Camera.main != null)
            {
                playerPos = Camera.main.transform.position;
                playerRot = new Vector2(Camera.main.transform.eulerAngles.x, Camera.main.transform.eulerAngles.y);
            }

            int seed = WorldManager.Instance.Config != null ? WorldManager.Instance.Config.Seed : 0;

            SaveSystem.Instance.SaveWorld(
                slotIndex,
                WorldManager.Instance.ActiveChunks,
                seed,
                playerPos,
                playerRot
            );

            RefreshSlotUI(slotIndex);
        }

        private void OnLoadSlotClicked(int slotIndex)
        {
            if (!SaveSystem.DoesSlotExist(slotIndex) || SaveSystem.Instance == null) return;

            WorldBlocksData blocksData = SaveSystem.Instance.LoadWorldBlocks(slotIndex);
            PlayerSaveData playerData = SaveSystem.Instance.LoadPlayerData(slotIndex);

            if (blocksData != null && WorldManager.Instance != null)
            {
                WorldManager.Instance.LoadWorldFromData(blocksData, playerData);
            }
        }
    }
}