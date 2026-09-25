using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

public class PlayerHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [SerializeField] private int maxHealth = 100;
    [SerializeField] private int currentHealth = 100;

    // Other scripts (like a death screen) can subscribe to this without us needing to edit this script later.
    public UnityEvent OnDeath;

    // Stops Die() from running more than once.
    private bool isDead = false;

    [Header("UI - Health Text")]
    // Drag your own Text object here (like the one from the health asset).
    // Leave this empty and one will be made automatically in the top left corner.
    [SerializeField] private Text healthText;

    [Header("UI - Health Bar")]
    // Drag the bar's "fill" Image here (the one you set to Image Type = Filled).
    // Leave this empty if you don't want a bar.
    [SerializeField] private Image healthBarFill;

    [Header("Health Bar Animation")]
    // Turn this on to make the bar slide smoothly instead of jumping instantly.
    [SerializeField] private bool smoothBarFill = true;
    // How fast the bar slides to the new value. Higher = faster.
    [SerializeField] private float barFillSpeed = 2f;

    // Where the bar SHOULD be (matches real health).
    private float targetFillAmount = 1f;
    // Where the bar IS RIGHT NOW on screen (slides toward targetFillAmount).
    private float displayedFillAmount = 1f;

    private void Start()
    {
        // Health always starts full.
        currentHealth = maxHealth;

        SetupHealthUI();
        UpdateHealthUI();
    }

    // private void Update()
    // {
    //     // TEMP TEST CODE - remove once there is a real way to take damage!
    //     // Press H to take 10 test damage.
    //     if (Keyboard.current[Key.H].wasPressedThisFrame)
    //     {
    //         TakeDamage(10);
    //     }
    // }

    private void Update()
    {
        // Slide the bar toward the real health value instead of snapping instantly.
        if (smoothBarFill && healthBarFill != null)
        {
            displayedFillAmount = Mathf.MoveTowards(displayedFillAmount, targetFillAmount, barFillSpeed * Time.deltaTime);
            healthBarFill.fillAmount = displayedFillAmount;
        }
    }

    public void TakeDamage(int amount)
    {
        if (isDead) return;

        currentHealth -= amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();

        if (currentHealth <= 0)
        {
            Die();
        }
    }

    public void Heal(int amount)
    {
        if (isDead) return;

        currentHealth += amount;
        currentHealth = Mathf.Clamp(currentHealth, 0, maxHealth);
        UpdateHealthUI();
    }

    private void Die()
    {
        if (isDead) return;
        isDead = true;

        // Let anything listening know the player died before we reload.
        OnDeath?.Invoke();

        // Simple restart for now - just reload the level we're already in.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }

    // Builds a basic Canvas + Text in the top left corner of the screen.
    private void SetupHealthUI()
    {
        if (healthText != null) return;

        Canvas canvas = FindAnyObjectByType<Canvas>();
        if (canvas == null)
        {
            GameObject canvasObject = new GameObject("HealthCanvas");
            canvas = canvasObject.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObject.AddComponent<CanvasScaler>();
            canvasObject.AddComponent<GraphicRaycaster>();
        }

        GameObject textObject = new GameObject("HealthText");
        textObject.transform.SetParent(canvas.transform, false);

        healthText = textObject.AddComponent<Text>();
        healthText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        healthText.fontSize = 32;
        healthText.color = Color.white;
        healthText.alignment = TextAnchor.UpperLeft;

        // Anchor to the top left corner of the screen.
        RectTransform rect = healthText.rectTransform;
        rect.anchorMin = new Vector2(0f, 1f);
        rect.anchorMax = new Vector2(0f, 1f);
        rect.pivot = new Vector2(0f, 1f);
        rect.anchoredPosition = new Vector2(20f, -20f);
        rect.sizeDelta = new Vector2(200f, 50f);
    }

    private void UpdateHealthUI()
    {
        if (healthText != null)
        {
            healthText.text = currentHealth + " / " + maxHealth;
        }

        // Work out how "full" the bar should be, as a value from 0 to 1.
        targetFillAmount = (float)currentHealth / maxHealth;

        // If we're not animating the bar, just snap it straight to the target.
        if (healthBarFill != null && !smoothBarFill)
        {
            displayedFillAmount = targetFillAmount;
            healthBarFill.fillAmount = displayedFillAmount;
        }
    }
}
