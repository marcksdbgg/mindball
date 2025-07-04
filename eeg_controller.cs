using UnityEngine;
using NativeWebSocket;
using System.Collections.Generic;

/// <summary>
/// Controlador para objetos 3D interactivos mediante una interfaz Cerebro-Computador (BCI-EEG).
/// Recibe datos de concentración vía WebSocket para controlar el movimiento vertical,
/// y proporciona feedback audiovisual dinámico al usuario. También gestiona la selección
/// de objetos para tareas de atención focalizada.
/// </summary>
public class WebSocketEEGController : MonoBehaviour
{
    [Header("Configuración de Interacción")]
    [Tooltip("Identificador único para este objeto (debe coincidir con el sistema BCI).")]
    public int objectId = 1;

    [Header("Parámetros de Movimiento")]
    [Tooltip("Altura máxima que el objeto puede alcanzar.")]
    public float maxHeight = 3f;
    [Tooltip("Velocidad de interpolación para el movimiento ascendente.")]
    public float moveLerpSpeed = 5f;
    [Tooltip("Velocidad de caída cuando no se detecta concentración.")]
    public float fallSpeed = 0.4f;

    [Header("Feedback Auditivo")]
    [Tooltip("Frecuencia mínima del tono en Hz.")]
    public float minFrequency = 200f;
    [Tooltip("Frecuencia máxima del tono en Hz.")]
    public float maxFrequency = 800f;
    [Tooltip("Volumen del audio.")]
    [Range(0f, 1f)]
    public float audioVolume = 0.5f;

    [Header("Feedback Visual")]
    [Tooltip("Intensidad de emisión mínima (sin concentración).")]
    public float minEmissionIntensity = 0f;
    [Tooltip("Intensidad de emisión máxima (concentración máxima).")]
    public float maxEmissionIntensity = 5f;
    [Tooltip("Color del efecto de brillo por emisión.")]
    public Color emissionColor = Color.cyan;

    [Header("Apariencia de Selección")]
    [Tooltip("Color del material cuando el objeto NO está seleccionado.")]
    public Color normalColor = Color.white;

    // Conexión WebSocket y estado
    private static WebSocket ws;
    private static bool wsInitialized = false;
    private static List<WebSocketEEGController> allControllers = new List<WebSocketEEGController>();

    // Estado del objeto
    private float currentConcentration = 0f;
    private float lastPacketTime;
    private bool isSelected = false;
    private bool canMove = false; // Flag para permitir movimiento solo al objeto seleccionado
    private const float NO_INPUT_THRESHOLD_SECONDS = 0.2f;

    // Componentes y materiales
    private Renderer objectRenderer;
    private Material dynamicMaterial;
    private AudioSource audioSource;
    private float currentFrequency = 0f;

    #region Ciclo de Vida de Unity y WebSocket

    private void Awake()
    {
        allControllers.Add(this);
        
        objectRenderer = GetComponent<Renderer>();
        if (objectRenderer == null)
        {
            Debug.LogError($"[Object ID: {objectId}] No se encontró un componente Renderer. Los efectos visuales no funcionarán.");
            return;
        }

        SetupAudioSource();
        CreateDynamicMaterial();
    }

    private void Start()
    {
        if (!wsInitialized)
        {
            InitializeWebSocket();
            wsInitialized = true;
        }

        // Seleccionar el primer objeto por defecto al iniciar la escena
        if (objectId == 1)
        {
            Invoke(nameof(SelectDefaultObject), 0.1f);
        }
    }

    private void Update()
    {
        ws?.DispatchMessageQueue();

        if (isSelected && canMove)
        {
            // El objeto cae si no se reciben datos de concentración recientes
            if (Time.time - lastPacketTime > NO_INPUT_THRESHOLD_SECONDS)
            {
                currentConcentration = Mathf.MoveTowards(currentConcentration, 0f, fallSpeed * Time.deltaTime);
            }

            MoveObject(currentConcentration);
            UpdateAudioFeedback(currentConcentration);
            UpdateVisualEffects(currentConcentration);
        }
        else
        {
            // Asegurarse de que los efectos estén desactivados si no está seleccionado
            StopEffects();
        }
    }
    
    private void OnDestroy()
    {
        allControllers.Remove(this);

        if (audioSource != null) Destroy(audioSource);
        if (dynamicMaterial != null) Destroy(dynamicMaterial);

        if (allControllers.Count == 0 && ws != null)
        {
            ws.Close();
            ws = null;
            wsInitialized = false;
        }
    }
    
    private static void InitializeWebSocket()
    {
        ws = new WebSocket("ws://127.0.0.1:4649");

        ws.OnOpen += () => Debug.Log("WebSocket Conectado: Sistema BCI-EEG listo.");
        ws.OnError += e => Debug.LogError($"Error en WebSocket: {e}");
        ws.OnClose += e => Debug.Log($"WebSocket Cerrado: Código {e}");

        ws.OnMessage += bytes =>
        {
            var json = System.Text.Encoding.UTF8.GetString(bytes);

            // Intenta procesar como mensaje de selección de objeto
            try
            {
                var selectionData = JsonUtility.FromJson<ObjectSelectionData>(json);
                if (selectionData != null && selectionData.type == "object_selection")
                {
                    HandleGlobalObjectSelection(selectionData.objectId);
                    return;
                }
            }
            catch { /* No es un mensaje de selección */ }

            // Intenta procesar como datos de EEG
            try
            {
                var eegData = JsonUtility.FromJson<EEGData>(json);
                if (eegData != null)
                {
                    HandleGlobalMovement(eegData.concentration);
                    return;
                }
            }
            catch { /* No es un mensaje de EEG */ }
        };

        _ = ws.Connect();
    }

    #endregion

    #region Manejo de Estado Global y Movimiento

    private void SelectDefaultObject()
    {
        HandleGlobalObjectSelection(1);
    }
    
    private static void HandleGlobalObjectSelection(int newSelectedId)
    {
        foreach (var controller in allControllers)
        {
            if (controller != null)
            {
                bool isNowSelected = controller.objectId == newSelectedId;
                controller.SetSelected(isNowSelected);
            }
        }
    }

    private static void HandleGlobalMovement(float concentrationValue)
    {
        foreach (var controller in allControllers)
        {
            // Solo el objeto con permiso para moverse reacciona a los datos de EEG
            if (controller != null && controller.canMove)
            {
                controller.currentConcentration = Mathf.Clamp01(concentrationValue);
                controller.lastPacketTime = Time.time;
                break; // Asumimos que solo un objeto puede ser controlado a la vez
            }
        }
    }

    private void SetSelected(bool selected)
    {
        isSelected = selected;
        canMove = selected;

        if (objectRenderer == null) return;

        if (selected)
        {
            // Aplicar material dinámico que reaccionará a los efectos
            objectRenderer.material = dynamicMaterial;
            dynamicMaterial.color = Color.white;
        }
        else
        {
            // Restaurar a un material simple no reactivo
            Material defaultMaterial = new Material(Shader.Find("Standard"))
            {
                color = normalColor,
            };
            defaultMaterial.SetFloat("_Metallic", 0.2f);
            defaultMaterial.SetFloat("_Glossiness", 0.5f);
            objectRenderer.material = defaultMaterial;

            // Apagar efectos y resetear posición
            StopEffects();
            currentConcentration = 0f;
            transform.position = new Vector3(transform.position.x, 1.5f, transform.position.z);
        }
    }

    private void MoveObject(float normalizedValue)
    {
        Vector3 currentPos = transform.position;
        float baseY = 1.5f; // Altura base en el suelo
        Vector3 targetPosition = new Vector3(
            currentPos.x,
            baseY + Mathf.Lerp(0f, maxHeight, normalizedValue),
            currentPos.z
        );

        transform.position = Vector3.Lerp(currentPos, targetPosition, Time.deltaTime * moveLerpSpeed);
    }

    #endregion

    #region Feedback Audio y Visual

    private void SetupAudioSource()
    {
        audioSource = gameObject.AddComponent<AudioSource>();
        audioSource.volume = audioVolume;
        audioSource.loop = true;
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 0.8f; // Sonido 3D
        audioSource.rolloffMode = AudioRolloffMode.Linear;
    }

    private void CreateDynamicMaterial()
    {
        if (objectRenderer.material != null)
        {
            dynamicMaterial = new Material(Shader.Find("Standard"));
            dynamicMaterial.EnableKeyword("_EMISSION");
            dynamicMaterial.SetFloat("_Metallic", 0.2f);
            dynamicMaterial.SetFloat("_Glossiness", 0.8f);
        }
    }

    private void UpdateAudioFeedback(float concentrationPercent)
    {
        if (audioSource == null) return;

        if (concentrationPercent > 0.05f)
        {
            if (!audioSource.isPlaying)
            {
                audioSource.Play();
            }

            float targetFrequency = Mathf.Lerp(minFrequency, maxFrequency, concentrationPercent);
            currentFrequency = Mathf.Lerp(currentFrequency, targetFrequency, Time.deltaTime * 2f);
            
            float dynamicVolume = audioVolume * Mathf.Lerp(0.3f, 1f, concentrationPercent);
            audioSource.volume = Mathf.Lerp(audioSource.volume, dynamicVolume, Time.deltaTime * 4f);

            UpdateProceduralTone(currentFrequency);
        }
        else
        {
            StopEffects();
        }
    }

    private void UpdateVisualEffects(float concentrationPercent)
    {
        if (dynamicMaterial == null) return;

        float emissionIntensity = Mathf.Lerp(minEmissionIntensity, maxEmissionIntensity, concentrationPercent);
        dynamicMaterial.SetColor("_EmissionColor", emissionColor * emissionIntensity);
    }
    
    private void StopEffects()
    {
        if (audioSource != null && audioSource.isPlaying)
        {
            audioSource.volume = Mathf.Lerp(audioSource.volume, 0f, Time.deltaTime * 3f);
            if (audioSource.volume < 0.01f)
            {
                audioSource.Stop();
            }
        }

        if (dynamicMaterial != null)
        {
            dynamicMaterial.SetColor("_EmissionColor", emissionColor * minEmissionIntensity);
        }
    }

    private void UpdateProceduralTone(float frequency)
    {
        if (audioSource.clip == null || Mathf.Abs(frequency - currentFrequency) > 10f)
        {
            int sampleRate = 44100;
            int samples = sampleRate / 10; // Clip corto de 0.1s para loopear
            float[] audioData = new float[samples];
            
            for (int i = 0; i < samples; i++)
            {
                audioData[i] = Mathf.Sin(2 * Mathf.PI * frequency * ((float)i / sampleRate));
            }

            AudioClip clip = AudioClip.Create("ProceduralTone", samples, 1, sampleRate, false);
            clip.SetData(audioData, 0);
            audioSource.clip = clip;
            if (audioSource.isPlaying) audioSource.Play();
        }
    }

    #endregion

    #region Estructuras de Datos para WebSocket

    /// <summary>
    /// Estructura para decodificar mensajes de selección de objeto.
    /// </summary>
    [System.Serializable]
    private class ObjectSelectionData
    {
        public string type;
        public int objectId;
    }

    /// <summary>
    /// Estructura para decodificar los datos de concentración del BCI.
    /// </summary>
    [System.Serializable]
    private class EEGData
    {
        // Se espera un valor normalizado [0, 1] desde el script de Python.
        public float concentration;
    }
    
    #endregion
}
