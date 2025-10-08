# Optimizador de imágenes WebP

Este repositorio incluye una aplicación de escritorio en WPF (.NET 7) que genera versiones optimizadas en formato **WebP** de todas las imágenes contenidas en una carpeta.

## Requisitos

- Visual Studio 2022 (17.4 o superior)
- SDK de .NET 7

## Ejecución

1. Abre `ImageOptimizerApp.sln` con Visual Studio 2022.
2. Compila y ejecuta el proyecto `ImageOptimizerApp`.
3. En la aplicación, selecciona la carpeta de origen que contiene las imágenes a optimizar.
4. Pulsa **Comenzar conversión** para generar las variantes:
   - `chico` (≈400px de ancho máximo)
   - `mediano` (≈800px de ancho máximo)
   - `grande` (≈1200px de ancho máximo)
5. Las imágenes convertidas se guardarán dentro de una subcarpeta llamada `resultado` en la misma carpeta de origen.

Durante el proceso se muestra un indicador de progreso, el nombre de la imagen en curso y el porcentaje completado.
