using UnityEngine;
using UnityEngine.UI;
using NativeWebSocket;
using System.Collections.Generic;

public class CalibradorBCI : MonoBehaviour
{
    public string websocketUrl = "ws://127.0.0.1:4649";

    [Header("UI")]
    public Slider alphaSlider;
    public Slider betaSlider;
    public Text feedbackText;
    public Text countdownText;

    [Header("Tiempos (segundos)")]
    public int tiempoRelax = 10;
    public int tiempoConcentra = 10;

    private WebSocket ws;
    private List<float> relajacionValores = new();
    private List<float> concentracionValores = new();

    private enum Estado { Esperando, Relajacion, Concentracion, Terminado }
    private Estado estadoActual = Estado.Esperando;

    private float tiempoRestante;
    private float ultimoValor = -1f;

    private void Start()
    {
        ws = new WebSocket(websocketUrl);

        ws.OnMessage += bytes =>
        {
            string json = System.Text.Encoding.UTF8.GetString(bytes);
            EEGData data = JsonUtility.FromJson<EEGData>(json);
            ultimoValor = Mathf.Clamp(data.value / 100f, 0f, 1f); // Normalizado [0,1]
        };

        ws.Connect();

        StartCoroutine(CalibrarProceso());
    }

    private void Update()
    {
        ws?.DispatchMessageQueue();

        if (estadoActual == Estado.Relajacion)
        {
            alphaSlider.value = ultimoValor;
            relajacionValores.Add(ultimoValor);
        }
        else if (estadoActual == Estado.Concentracion)
        {
            betaSlider.value = ultimoValor;
            concentracionValores.Add(ultimoValor);
        }

        if (estadoActual != Estado.Terminado)
        {
            tiempoRestante -= Time.deltaTime;
            countdownText.text = "Tiempo restante: " + Mathf.CeilToInt(tiempoRestante) + "s";
        }
    }

    private System.Collections.IEnumerator CalibrarProceso()
    {
        feedbackText.text = "Prepárate para RELAJARTE...";
        yield return new WaitForSeconds(3f);

        estadoActual = Estado.Relajacion;
        tiempoRestante = tiempoRelax;
        feedbackText.text = "Relájate. Cierra los ojos si quieres.";

        yield return new WaitForSeconds(tiempoRelax);

        estadoActual = Estado.Concentracion;
        tiempoRestante = tiempoConcentra;
        feedbackText.text = "¡Ahora CONCÉNTRATE! Piensa intensamente.";

        yield return new WaitForSeconds(tiempoConcentra);

        estadoActual = Estado.Terminado;
        feedbackText.text = "¡Calibración completa!";
        countdownText.text = "";

        float minVal = Mathf.Clamp01(Promedio(relajacionValores));
        float maxVal = Mathf.Clamp01(Promedio(concentracionValores));

        PlayerPrefs.SetFloat("BCI_MIN", minVal);
        PlayerPrefs.SetFloat("BCI_MAX", maxVal);
        PlayerPrefs.Save();

        Debug.Log($"Calibración guardada: MIN={minVal}, MAX={maxVal}");
    }

    private float Promedio(List<float> valores)
    {
        if (valores.Count == 0) return 0f;
        float suma = 0f;
        foreach (var v in valores) suma += v;
        return suma / valores.Count;
    }

    private void OnDestroy()
    {
        ws?.Close();
    }

    [System.Serializable]
    public class EEGData
    {
        public int value;
    }
}
